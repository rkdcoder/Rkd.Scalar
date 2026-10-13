using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace Rkd.Scalar.HttpLogging.Postgres
{
    /// <summary>
    /// Writes each batch with one binary <c>COPY</c> (a single round trip, no per-row INSERT). Only the columns that
    /// exist in the table, with a compatible type, are written, so a table with fewer columns (or extra ones with
    /// defaults) works. Called by the background writer of <c>WithHttpLogging()</c>, never by a request.
    /// </summary>
    internal sealed partial class PostgresHttpLogSink : IHttpLogSink
    {
        /// <summary>A column of the table that receives a value, with the size limit and JSON type found in the database.</summary>
        internal sealed record Target(PostgresHttpLogTable.Column Column, NpgsqlDbType DbType, int? MaxLength);

        private readonly string _connectionString;

        private readonly PostgresHttpLogOptions _options;

        private readonly PostgresTableName _table;

        private readonly ILogger<PostgresHttpLogSink> _logger;

        private Target[]? _targets;

        public PostgresHttpLogSink(string connectionString, PostgresHttpLogOptions options, ILogger<PostgresHttpLogSink> logger)
        {
            _connectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                CommandTimeout = options.CommandTimeoutSeconds
            }.ConnectionString;
            _options = options;
            _table = PostgresTableName.Parse(options.Table);
            _logger = logger;
        }

        public async Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var targets = _targets ??= await LoadTargetsAsync(connection, cancellationToken);

            var columns = string.Join(", ", targets.Select(t => $"\"{t.Column.Name}\""));

            try
            {
                await using var writer = await connection.BeginBinaryImportAsync(
                    $"COPY {_table.Quoted} ({columns}) FROM STDIN (FORMAT BINARY)", cancellationToken);

                foreach (var entry in entries)
                {
                    await writer.StartRowAsync(cancellationToken);

                    foreach (var target in targets)
                    {
                        var value = Prepare(target, entry);

                        if (value is null)
                            await writer.WriteNullAsync(cancellationToken);
                        else
                            await WriteAsync(writer, target, value, cancellationToken);
                    }
                }

                await writer.CompleteAsync(cancellationToken);
            }
            catch (NpgsqlException)
            {
                // The table may have changed (recreated, columns removed): read it again on the next batch.
                _targets = null;
                throw;
            }
        }

        /// <summary>The value of a column, cut to its size and without NUL characters (rejected by PostgreSQL text and jsonb).</summary>
        internal static object? Prepare(Target target, HttpLogEntry entry)
        {
            var value = target.Column.Value(entry);

            if (value is not string text)
                return value;

            if (text.Contains('\0', StringComparison.Ordinal))
                text = text.Replace("\0", string.Empty, StringComparison.Ordinal);

            // Cut by characters, not in the middle of a surrogate pair.
            if (target.MaxLength is { } max && text.Length > max)
                text = char.IsHighSurrogate(text[max - 1]) ? text[..(max - 1)] : text[..max];

            return text;
        }

        private static ValueTask WriteAsync(NpgsqlBinaryImporter writer, Target target, object value, CancellationToken cancellationToken) =>
            target.Column.Kind switch
            {
                PostgresHttpLogTable.ColumnKind.Double => new ValueTask(writer.WriteAsync((double)value, NpgsqlDbType.Double, cancellationToken)),
                PostgresHttpLogTable.ColumnKind.Int => new ValueTask(writer.WriteAsync((int)value, NpgsqlDbType.Integer, cancellationToken)),
                PostgresHttpLogTable.ColumnKind.BigInt => new ValueTask(writer.WriteAsync((long)value, NpgsqlDbType.Bigint, cancellationToken)),
                PostgresHttpLogTable.ColumnKind.Bool => new ValueTask(writer.WriteAsync((bool)value, NpgsqlDbType.Boolean, cancellationToken)),
                PostgresHttpLogTable.ColumnKind.Timestamp => new ValueTask(writer.WriteAsync((DateTime)value, NpgsqlDbType.TimestampTz, cancellationToken)),
                _ => new ValueTask(writer.WriteAsync((string)value, target.DbType, cancellationToken))
            };

        private async Task<Target[]> LoadTargetsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            var existing = await ReadColumnsAsync(connection, cancellationToken);

            if (existing.Count == 0)
            {
                if (!_options.CreateTable)
                    throw new InvalidOperationException(
                        $"The HTTP log table {_table.Quoted} does not exist. Create it with PostgresHttpLogTable.CreateScript(\"{_options.Table}\") " +
                        "or set CreateTable = true.");

                await using (var create = new NpgsqlCommand(PostgresHttpLogTable.CreateScript(_options.Table), connection))
                {
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }

                LogTableCreated(_logger, _table.Quoted);
                existing = await ReadColumnsAsync(connection, cancellationToken);
            }

            var targets = new List<Target>();
            var ignored = new List<string>();

            foreach (var column in PostgresHttpLogTable.Columns)
            {
                if (existing.TryGetValue(column.Name, out var found) && DbTypeOf(column.Kind, found.Type) is { } dbType)
                    targets.Add(new Target(column, dbType, found.MaxLength));
                else
                    ignored.Add(column.Name);
            }

            if (ignored.Count > 0)
                LogColumnsIgnored(_logger, _table.Quoted, string.Join(", ", ignored));

            return targets.ToArray();
        }

        /// <summary>The type Npgsql writes for a column of the database, or <see langword="null"/> when it cannot hold the value.</summary>
        private static NpgsqlDbType? DbTypeOf(PostgresHttpLogTable.ColumnKind kind, string udt) => kind switch
        {
            PostgresHttpLogTable.ColumnKind.Text => udt is "text" or "varchar" or "bpchar" ? NpgsqlDbType.Text : null,
            PostgresHttpLogTable.ColumnKind.Json => udt switch
            {
                "jsonb" => NpgsqlDbType.Jsonb,
                "json" => NpgsqlDbType.Json,
                "text" or "varchar" or "bpchar" => NpgsqlDbType.Text,
                _ => null
            },
            PostgresHttpLogTable.ColumnKind.Timestamp => udt == "timestamptz" ? NpgsqlDbType.TimestampTz : null,
            PostgresHttpLogTable.ColumnKind.Double => udt == "float8" ? NpgsqlDbType.Double : null,
            PostgresHttpLogTable.ColumnKind.Int => udt == "int4" ? NpgsqlDbType.Integer : null,
            PostgresHttpLogTable.ColumnKind.BigInt => udt == "int8" ? NpgsqlDbType.Bigint : null,
            PostgresHttpLogTable.ColumnKind.Bool => udt == "bool" ? NpgsqlDbType.Boolean : null,
            _ => null
        };

        private async Task<Dictionary<string, (string Type, int? MaxLength)>> ReadColumnsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                "SELECT column_name::text, udt_name::text, character_maximum_length FROM information_schema.columns " +
                "WHERE table_schema = @schema AND table_name = @table", connection);
            command.Parameters.Add(new NpgsqlParameter("schema", NpgsqlDbType.Text) { Value = _table.Schema });
            command.Parameters.Add(new NpgsqlParameter("table", NpgsqlDbType.Text) { Value = _table.Table });

            var columns = new Dictionary<string, (string, int?)>(StringComparer.Ordinal);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                columns[reader.GetString(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2));

            return columns;
        }

        [LoggerMessage(EventId = 50, Level = LogLevel.Information, Message = "HTTP log table {Table} created.")]
        private static partial void LogTableCreated(ILogger logger, string table);

        [LoggerMessage(EventId = 51, Level = LogLevel.Information, Message = "HTTP log table {Table} has no compatible column(s) {Columns}; these values are not written.")]
        private static partial void LogColumnsIgnored(ILogger logger, string table, string columns);
    }
}
