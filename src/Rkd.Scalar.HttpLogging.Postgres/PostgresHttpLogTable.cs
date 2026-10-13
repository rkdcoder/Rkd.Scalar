using System.Text;
using System.Text.Json;

namespace Rkd.Scalar
{
    /// <summary>
    /// Table used by the PostgreSQL HTTP log sink: its columns and the script that creates it.
    /// </summary>
    public static class PostgresHttpLogTable
    {
        internal enum ColumnKind { Text, Timestamp, Double, Int, BigInt, Bool, Json }

        internal sealed record Column(string Name, ColumnKind Kind, bool Nullable, Func<HttpLogEntry, object?> Value);

        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        internal static readonly Column[] Columns =
        [
            new("trace_id", ColumnKind.Text, false, e => e.TraceId),
            new("started_at", ColumnKind.Timestamp, false, e => e.StartedAt.UtcDateTime),
            new("duration_ms", ColumnKind.Double, false, e => e.Duration.TotalMilliseconds),
            new("application_name", ColumnKind.Text, true, e => e.ApplicationName),
            new("method", ColumnKind.Text, false, e => e.Method),
            new("scheme", ColumnKind.Text, true, e => e.Scheme),
            new("host", ColumnKind.Text, true, e => e.Host),
            new("path_base", ColumnKind.Text, true, e => e.PathBase),
            new("path", ColumnKind.Text, false, e => e.Path),
            new("query_string", ColumnKind.Text, true, e => e.QueryString),
            new("route_pattern", ColumnKind.Text, true, e => e.RoutePattern),
            new("protocol", ColumnKind.Text, true, e => e.Protocol),
            new("status_code", ColumnKind.Int, false, e => e.StatusCode),
            new("is_success", ColumnKind.Bool, false, e => e.IsSuccess),
            new("error_code", ColumnKind.Text, true, e => e.ErrorCode),
            new("exception", ColumnKind.Text, true, e => e.Exception),
            new("user_name", ColumnKind.Text, true, e => e.UserName),
            new("user_id", ColumnKind.Text, true, e => e.UserId),
            new("client_ip", ColumnKind.Text, true, e => e.ClientIp),
            new("client_port", ColumnKind.Int, true, e => e.ClientPort),
            new("local_ip", ColumnKind.Text, true, e => e.LocalIp),
            new("local_port", ColumnKind.Int, true, e => e.LocalPort),
            new("connection_id", ColumnKind.Text, true, e => e.ConnectionId),
            new("user_agent", ColumnKind.Text, true, e => e.UserAgent),
            new("referer", ColumnKind.Text, true, e => e.Referer),
            new("locale", ColumnKind.Text, true, e => e.Locale),
            new("request_headers", ColumnKind.Json, true, e => e.RequestHeaders.Count == 0 ? null : Serialize(e.RequestHeaders)),
            new("request_content_type", ColumnKind.Text, true, e => e.RequestContentType),
            new("request_content_length", ColumnKind.BigInt, true, e => e.RequestContentLength),
            new("request_body", ColumnKind.Text, true, e => e.RequestBody),
            new("request_body_truncated", ColumnKind.Bool, false, e => e.RequestBodyTruncated),
            new("response_content_type", ColumnKind.Text, true, e => e.ResponseContentType),
            new("response_size", ColumnKind.BigInt, true, e => e.ResponseSize),
            new("response_body", ColumnKind.Text, true, e => e.ResponseBody),
            new("response_body_truncated", ColumnKind.Bool, false, e => e.ResponseBodyTruncated),
            new("properties", ColumnKind.Json, true, e => e.Properties.Count == 0 ? null : Serialize(e.Properties))
        ];

        /// <summary>
        /// Returns the script that creates the table (and its schema), with indexes on <c>started_at</c> and
        /// <c>trace_id</c> — the <c>traceId</c> of an error response finds its log row.
        /// </summary>
        /// <param name="table">Table name, optionally with schema. Defaults to <c>public.http_logs</c>.</param>
        /// <returns>SQL script, idempotent.</returns>
        public static string CreateScript(string table = "public.http_logs")
        {
            var name = PostgresTableName.Parse(table);
            var sql = new StringBuilder();

            if (name.Schema != "public")
                sql.Append("CREATE SCHEMA IF NOT EXISTS ").Append(name.QuotedSchema).AppendLine(";");

            sql.Append("CREATE TABLE IF NOT EXISTS ").Append(name.Quoted).AppendLine(" (");
            sql.AppendLine("    \"id\" bigint GENERATED ALWAYS AS IDENTITY NOT NULL,");

            foreach (var column in Columns)
            {
                sql.Append("    \"").Append(column.Name).Append("\" ").Append(SqlType(column.Kind))
                   .Append(column.Nullable ? " NULL" : " NOT NULL").AppendLine(",");
            }

            sql.Append("    CONSTRAINT \"pk_").Append(name.IndexSuffix).AppendLine("\" PRIMARY KEY (\"id\")");
            sql.AppendLine(");");
            sql.Append("CREATE INDEX IF NOT EXISTS \"ix_").Append(name.IndexSuffix).Append("_started_at\" ON ").Append(name.Quoted).AppendLine(" (\"started_at\");");
            sql.Append("CREATE INDEX IF NOT EXISTS \"ix_").Append(name.IndexSuffix).Append("_trace_id\" ON ").Append(name.Quoted).AppendLine(" (\"trace_id\");");

            return sql.ToString();
        }

        private static string SqlType(ColumnKind kind) => kind switch
        {
            ColumnKind.Text => "text",
            ColumnKind.Timestamp => "timestamptz",
            ColumnKind.Double => "double precision",
            ColumnKind.Int => "integer",
            ColumnKind.BigInt => "bigint",
            ColumnKind.Bool => "boolean",
            ColumnKind.Json => "jsonb",
            _ => throw new NotSupportedException(kind.ToString())
        };

        /// <summary>JSON text without the <c>\u0000</c> escape: <c>jsonb</c> rejects it and would reject the whole batch.</summary>
        private static string Serialize<T>(T value)
        {
            const string nul = "\\u0000";

            var json = JsonSerializer.Serialize(value, Json);
            var at = json.IndexOf(nul, StringComparison.Ordinal);

            if (at < 0)
                return json;

            var result = new StringBuilder(json.Length);
            var last = 0;

            for (; at >= 0; at = json.IndexOf(nul, at + 1, StringComparison.Ordinal))
            {
                var backslashes = 0;

                while (at - 1 - backslashes >= 0 && json[at - 1 - backslashes] == '\\')
                    backslashes++;

                // An odd run of backslashes means this one is escaped (a literal "\u0000" text), not a NUL.
                if (backslashes % 2 == 1 || at < last)
                    continue;

                result.Append(json, last, at - last);
                last = at + nul.Length;
            }

            return result.Append(json, last, json.Length - last).ToString();
        }
    }

    /// <summary>A validated <c>schema.table</c> name. Unquoted names are lower-cased, as PostgreSQL does.</summary>
    internal sealed record PostgresTableName(string Schema, string Table)
    {
        private const int MaxLength = 63;

        public string Quoted => $"\"{Schema}\".\"{Table}\"";

        public string QuotedSchema => $"\"{Schema}\"";

        /// <summary>Index and constraint names are limited to 63 characters.</summary>
        public string IndexSuffix
        {
            get
            {
                var suffix = Schema == "public" ? Table : $"{Schema}_{Table}";
                return suffix.Length > 49 ? suffix[..49] : suffix;
            }
        }

        public static PostgresTableName Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("The HTTP log table name cannot be empty.", nameof(value));

            var parts = value.Trim().Split('.');

            if (parts.Length > 2)
                throw new ArgumentException($"'{value}' is not a valid table name. Use 'table' or 'schema.table'.", nameof(value));

            var names = parts.Select(part => Unquote(part, value)).ToArray();

            return names.Length == 1 ? new PostgresTableName("public", names[0]) : new PostgresTableName(names[0], names[1]);
        }

        private static string Unquote(string part, string value)
        {
            var name = part.Trim();
            var quoted = name.Length >= 2 && name.StartsWith('"') && name.EndsWith('"');

            if (quoted)
                name = name[1..^1];

            if (name.Length == 0 || name.Length > MaxLength || char.IsDigit(name[0]) || name.Any(c => !(char.IsLetterOrDigit(c) || c is '_' or '$')))
                throw new ArgumentException(
                    $"'{value}' is not a valid table name: use letters, digits and '_' (e.g. 'logs.http_requests').",
                    nameof(value));

            return quoted ? name : name.ToLowerInvariant();
        }
    }
}
