namespace Rkd.Scalar
{
    /// <summary>
    /// Options of <c>WriteHttpLogsToSqlServer</c>. Bound from the <c>HttpLogging:SqlServer</c> configuration section
    /// when it exists.
    /// </summary>
    /// <example>
    /// <code>
    /// "HttpLogging": {
    ///   "SqlServer": { "ConnectionStringName": "Logs", "Table": "logs.HttpRequests", "CreateTable": true }
    /// }
    /// </code>
    /// </example>
    public sealed class SqlServerHttpLogOptions
    {
        /// <summary>Connection string. Use <see cref="ConnectionStringName"/> to read it from <c>ConnectionStrings</c>.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>Name of an entry of the <c>ConnectionStrings</c> section (used when <see cref="ConnectionString"/> is empty).</summary>
        public string? ConnectionStringName { get; set; }

        /// <summary>Table, optionally with its schema (<c>logs.HttpRequests</c>, <c>[logs].[HttpRequests]</c>). Defaults to <c>dbo.HttpLogs</c>.</summary>
        public string Table { get; set; } = "dbo.HttpLogs";

        /// <summary>
        /// Creates the table (and its schema) on the first write when it does not exist. Requires DDL permissions;
        /// in restricted environments, create it once with <c>SqlServerHttpLogTable.CreateScript(...)</c>.
        /// Defaults to <see langword="false"/>.
        /// </summary>
        public bool CreateTable { get; set; }

        /// <summary>Timeout of each batch insert, in seconds. Defaults to 30.</summary>
        public int CommandTimeoutSeconds { get; set; } = 30;
    }
}
