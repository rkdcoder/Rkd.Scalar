using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Rkd.Scalar.HttpLogging.SqlServer
{
    /// <summary>
    /// Writes each batch with one <see cref="SqlBulkCopy"/> (a single round trip, no per-row INSERT). Only the
    /// columns that exist in the table are written, so a table with fewer columns (or extra ones with defaults)
    /// works. Called by the background writer of <c>WithHttpLogging()</c>, never by a request.
    /// </summary>
    internal sealed partial class SqlServerHttpLogSink : IHttpLogSink
    {
        private readonly string _connectionString;

        private readonly SqlServerHttpLogOptions _options;

        private readonly SqlTableName _table;

        private readonly ILogger<SqlServerHttpLogSink> _logger;

        private SqlServerHttpLogTable.Column[]? _columns;

        public SqlServerHttpLogSink(string connectionString, SqlServerHttpLogOptions options, ILogger<SqlServerHttpLogSink> logger)
        {
            _connectionString = connectionString;
            _options = options;
            _table = SqlTableName.Parse(options.Table);
            _logger = logger;
        }

        public async Task WriteAsync(IReadOnlyList<HttpLogEntry> entries, CancellationToken cancellationToken)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var columns = _columns ??= await LoadColumnsAsync(connection, cancellationToken);

            using var data = ToDataTable(entries, columns);
            using var bulk = new SqlBulkCopy(connection)
            {
                DestinationTableName = _table.Quoted,
                BatchSize = entries.Count,
                BulkCopyTimeout = _options.CommandTimeoutSeconds
            };

            foreach (var column in columns)
                bulk.ColumnMappings.Add(column.Name, column.Name);

            try
            {
                await bulk.WriteToServerAsync(data, cancellationToken);
            }
            catch (SqlException)
            {
                // The table may have changed (recreated, columns removed): read it again on the next batch.
                _columns = null;
                throw;
            }
        }

        internal static DataTable ToDataTable(IReadOnlyList<HttpLogEntry> entries, IReadOnlyList<SqlServerHttpLogTable.Column> columns)
        {
            var table = new DataTable();

            foreach (var column in columns)
                table.Columns.Add(column.Name, ClrType(column.Type));

            foreach (var entry in entries)
            {
                var row = table.NewRow();

                foreach (var column in columns)
                    row[column.Name] = Normalize(column, column.Value(entry)) ?? DBNull.Value;

                table.Rows.Add(row);
            }

            return table;
        }

        /// <summary>Cuts texts to the column size: one oversized value must not reject the whole batch.</summary>
        private static object? Normalize(SqlServerHttpLogTable.Column column, object? value) =>
            value is string text && column.Size > 0 && text.Length > column.Size ? text[..column.Size] : value;

        private static Type ClrType(SqlDbType type) => type switch
        {
            SqlDbType.NVarChar or SqlDbType.VarChar => typeof(string),
            SqlDbType.DateTimeOffset => typeof(DateTimeOffset),
            SqlDbType.Float => typeof(double),
            SqlDbType.Int => typeof(int),
            SqlDbType.BigInt => typeof(long),
            SqlDbType.Bit => typeof(bool),
            _ => typeof(object)
        };

        private async Task<SqlServerHttpLogTable.Column[]> LoadColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            var existing = await ReadColumnsAsync(connection, cancellationToken);

            if (existing.Count == 0)
            {
                if (!_options.CreateTable)
                    throw new InvalidOperationException(
                        $"The HTTP log table {_table.Quoted} does not exist. Create it with SqlServerHttpLogTable.CreateScript(\"{_options.Table}\") " +
                        "or set CreateTable = true.");

                await using (var create = new SqlCommand(SqlServerHttpLogTable.CreateScript(_options.Table), connection))
                {
                    create.CommandTimeout = _options.CommandTimeoutSeconds;
                    await create.ExecuteNonQueryAsync(cancellationToken);
                }

                LogTableCreated(_logger, _table.Quoted);
                existing = await ReadColumnsAsync(connection, cancellationToken);
            }

            var columns = SqlServerHttpLogTable.Columns.Where(c => existing.Contains(c.Name)).ToArray();

            if (columns.Length < SqlServerHttpLogTable.Columns.Length)
            {
                var missing = SqlServerHttpLogTable.Columns.Where(c => !existing.Contains(c.Name)).Select(c => c.Name);
                LogColumnsIgnored(_logger, _table.Quoted, string.Join(", ", missing));
            }

            return columns;
        }

        private async Task<HashSet<string>> ReadColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            await using var command = new SqlCommand("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@table, N'U')", connection);
            command.Parameters.Add(new SqlParameter("@table", SqlDbType.NVarChar, 300) { Value = _table.Quoted });

            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(0));

            return columns;
        }

        [LoggerMessage(EventId = 40, Level = LogLevel.Information, Message = "HTTP log table {Table} created.")]
        private static partial void LogTableCreated(ILogger logger, string table);

        [LoggerMessage(EventId = 41, Level = LogLevel.Information, Message = "HTTP log table {Table} has no column(s) {Columns}; these values are not written.")]
        private static partial void LogColumnsIgnored(ILogger logger, string table, string columns);
    }
}
