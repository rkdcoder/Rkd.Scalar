namespace Rkd.Scalar
{
    /// <summary>
    /// Options of <c>WriteHttpLogsToPostgres</c>. Bound from the <c>HttpLogging:Postgres</c> configuration section
    /// when it exists.
    /// </summary>
    /// <example>
    /// <code>
    /// "HttpLogging": {
    ///   "Postgres": { "ConnectionStringName": "Logs", "Table": "logs.http_requests", "CreateTable": true }
    /// }
    /// </code>
    /// </example>
    public sealed class PostgresHttpLogOptions
    {
        /// <summary>Connection string. Use <see cref="ConnectionStringName"/> to read it from <c>ConnectionStrings</c>.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>Name of an entry of the <c>ConnectionStrings</c> section (used when <see cref="ConnectionString"/> is empty).</summary>
        public string? ConnectionStringName { get; set; }

        /// <summary>
        /// Table, optionally with its schema (<c>logs.http_requests</c>, <c>"Logs"."HttpRequests"</c>). Unquoted names are
        /// lower-cased, as PostgreSQL does; quote a name to keep its case. Defaults to <c>public.http_logs</c>.
        /// </summary>
        public string Table { get; set; } = "public.http_logs";

        /// <summary>
        /// Creates the table (and its schema) on the first write when it does not exist. Requires DDL permissions;
        /// in restricted environments, create it once with <c>PostgresHttpLogTable.CreateScript(...)</c>.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool CreateTable { get; set; }

        /// <summary>Timeout of each batch insert, in seconds. Defaults to 30.</summary>
        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
