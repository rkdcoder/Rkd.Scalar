using System.Data;
using System.Text;
using System.Text.Json;

namespace Rkd.Scalar
{
    /// <summary>
    /// Table used by the SQL Server HTTP log sink: its columns and the script that creates it.
    /// </summary>
    public static class SqlServerHttpLogTable
    {
        internal sealed record Column(string Name, SqlDbType Type, int Size, bool Nullable, Func<HttpLogEntry, object?> Value);

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        internal static readonly Column[] Columns =
        [
            new("TraceId", SqlDbType.NVarChar, 128, false, e => e.TraceId),
            new("StartedAt", SqlDbType.DateTimeOffset, 0, false, e => e.StartedAt),
            new("DurationMs", SqlDbType.Float, 0, false, e => e.Duration.TotalMilliseconds),
            new("ApplicationName", SqlDbType.NVarChar, 256, true, e => e.ApplicationName),
            new("Method", SqlDbType.VarChar, 16, false, e => e.Method),
            new("Scheme", SqlDbType.VarChar, 8, true, e => e.Scheme),
            new("Host", SqlDbType.NVarChar, 256, true, e => e.Host),
            new("PathBase", SqlDbType.NVarChar, 256, true, e => e.PathBase),
            new("Path", SqlDbType.NVarChar, 2048, false, e => e.Path),
            new("QueryString", SqlDbType.NVarChar, -1, true, e => e.QueryString),
            new("RoutePattern", SqlDbType.NVarChar, 512, true, e => e.RoutePattern),
            new("Protocol", SqlDbType.VarChar, 16, true, e => e.Protocol),
            new("StatusCode", SqlDbType.Int, 0, false, e => e.StatusCode),
            new("IsSuccess", SqlDbType.Bit, 0, false, e => e.IsSuccess),
            new("ErrorCode", SqlDbType.NVarChar, 128, true, e => e.ErrorCode),
            new("Exception", SqlDbType.NVarChar, -1, true, e => e.Exception),
            new("UserName", SqlDbType.NVarChar, 256, true, e => e.UserName),
            new("UserId", SqlDbType.NVarChar, 256, true, e => e.UserId),
            new("ClientIp", SqlDbType.VarChar, 64, true, e => e.ClientIp),
            new("ClientPort", SqlDbType.Int, 0, true, e => e.ClientPort),
            new("LocalIp", SqlDbType.VarChar, 64, true, e => e.LocalIp),
            new("LocalPort", SqlDbType.Int, 0, true, e => e.LocalPort),
            new("ConnectionId", SqlDbType.VarChar, 64, true, e => e.ConnectionId),
            new("UserAgent", SqlDbType.NVarChar, 1024, true, e => e.UserAgent),
            new("Referer", SqlDbType.NVarChar, 2048, true, e => e.Referer),
            new("Locale", SqlDbType.NVarChar, 256, true, e => e.Locale),
            new("RequestHeaders", SqlDbType.NVarChar, -1, true, e => e.RequestHeaders.Count == 0 ? null : JsonSerializer.Serialize(e.RequestHeaders, Json)),
            new("RequestContentType", SqlDbType.NVarChar, 256, true, e => e.RequestContentType),
            new("RequestContentLength", SqlDbType.BigInt, 0, true, e => e.RequestContentLength),
            new("RequestBody", SqlDbType.NVarChar, -1, true, e => e.RequestBody),
            new("RequestBodyTruncated", SqlDbType.Bit, 0, false, e => e.RequestBodyTruncated),
            new("ResponseContentType", SqlDbType.NVarChar, 256, true, e => e.ResponseContentType),
            new("ResponseSize", SqlDbType.BigInt, 0, true, e => e.ResponseSize),
            new("ResponseBody", SqlDbType.NVarChar, -1, true, e => e.ResponseBody),
            new("ResponseBodyTruncated", SqlDbType.Bit, 0, false, e => e.ResponseBodyTruncated),
            new("Properties", SqlDbType.NVarChar, -1, true, e => e.Properties.Count == 0 ? null : JsonSerializer.Serialize(e.Properties, Json))
        ];

        /// <summary>
        /// Returns the script that creates the table (and its schema), with indexes on <c>StartedAt</c> and
        /// <c>TraceId</c> — the <c>traceId</c> of an error response finds its log row.
        /// </summary>
        /// <param name="table">Table name, optionally with schema. Defaults to <c>dbo.HttpLogs</c>.</param>
        /// <returns>T-SQL script, idempotent.</returns>
        public static string CreateScript(string table = "dbo.HttpLogs")
        {
            var name = SqlTableName.Parse(table);
            var sql = new StringBuilder();

            sql.Append("IF SCHEMA_ID(N'").Append(name.SchemaLiteral).Append("') IS NULL EXEC(N'CREATE SCHEMA ")
               .Append(name.QuotedSchema.Replace("'", "''", StringComparison.Ordinal)).AppendLine("');");

            sql.Append("IF OBJECT_ID(N'").Append(name.QuotedLiteral).AppendLine("', N'U') IS NULL");
            sql.AppendLine("BEGIN");
            sql.Append("    CREATE TABLE ").Append(name.Quoted).AppendLine(" (");
            sql.AppendLine("        [Id] BIGINT IDENTITY(1,1) NOT NULL,");

            foreach (var column in Columns)
            {
                sql.Append("        [").Append(column.Name).Append("] ").Append(SqlType(column))
                   .Append(column.Nullable ? " NULL" : " NOT NULL").AppendLine(",");
            }

            sql.Append("        CONSTRAINT [PK_").Append(name.IndexSuffix).AppendLine("] PRIMARY KEY CLUSTERED ([Id])");
            sql.AppendLine("    );");
            sql.Append("    CREATE INDEX [IX_").Append(name.IndexSuffix).Append("_StartedAt] ON ").Append(name.Quoted).AppendLine(" ([StartedAt]);");
            sql.Append("    CREATE INDEX [IX_").Append(name.IndexSuffix).Append("_TraceId] ON ").Append(name.Quoted).AppendLine(" ([TraceId]);");
            sql.AppendLine("END;");

            return sql.ToString();
        }

        private static string SqlType(Column column) => column.Type switch
        {
            SqlDbType.NVarChar => column.Size < 0 ? "NVARCHAR(MAX)" : $"NVARCHAR({column.Size})",
            SqlDbType.VarChar => column.Size < 0 ? "VARCHAR(MAX)" : $"VARCHAR({column.Size})",
            SqlDbType.DateTimeOffset => "DATETIMEOFFSET(7)",
            SqlDbType.Float => "FLOAT",
            SqlDbType.Int => "INT",
            SqlDbType.BigInt => "BIGINT",
            SqlDbType.Bit => "BIT",
            _ => throw new NotSupportedException(column.Type.ToString())
        };
    }

    /// <summary>A validated <c>schema.table</c> name.</summary>
    internal sealed record SqlTableName(string Schema, string Table)
    {
        public string Quoted => $"[{Schema}].[{Table}]";

        public string QuotedSchema => $"[{Schema}]";

        /// <summary>Quoted name inside an N'...' literal.</summary>
        public string QuotedLiteral => Quoted.Replace("'", "''", StringComparison.Ordinal);

        public string SchemaLiteral => Schema.Replace("'", "''", StringComparison.Ordinal);

        public string IndexSuffix => Schema == "dbo" ? Table : $"{Schema}_{Table}";

        public static SqlTableName Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("The HTTP log table name cannot be empty.", nameof(value));

            var parts = value.Trim().Split('.');

            if (parts.Length > 2)
                throw new ArgumentException($"'{value}' is not a valid table name. Use 'table' or 'schema.table'.", nameof(value));

            var names = parts.Select(part => Unquote(part, value)).ToArray();

            return names.Length == 1 ? new SqlTableName("dbo", names[0]) : new SqlTableName(names[0], names[1]);
        }

        private static string Unquote(string part, string value)
        {
            var name = part.Trim();

            if (name.StartsWith('[') && name.EndsWith(']'))
                name = name[1..^1];

            if (name.Length == 0 || name.Length > 128 || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '$' or '#' or '@')))
                throw new ArgumentException(
                    $"'{value}' is not a valid table name: use letters, digits and '_' (e.g. 'logs.HttpRequests').",
                    nameof(value));

            return name;
        }
    }
}
