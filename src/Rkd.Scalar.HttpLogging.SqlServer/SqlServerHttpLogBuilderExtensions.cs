using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rkd.Scalar.HttpLogging.SqlServer;

namespace Rkd.Scalar
{
    /// <summary>
    /// Writes the HTTP logs of Rkd.Scalar to SQL Server.
    /// </summary>
    public static class SqlServerHttpLogBuilderExtensions
    {
        /// <summary>Configuration section of <see cref="SqlServerHttpLogOptions"/>.</summary>
        public const string SectionName = "HttpLogging:SqlServer";

        /// <summary>
        /// Writes the HTTP logs (<c>WithHttpLogging()</c>, enabled with its defaults when needed) to SQL Server, in
        /// batches, from a background service. Options come from the <c>HttpLogging:SqlServer</c> section, then
        /// <paramref name="configure"/>.
        /// </summary>
        /// <param name="builder">The Rkd.Scalar builder.</param>
        /// <param name="configure">Connection, table and table creation.</param>
        /// <returns>The same builder.</returns>
        /// <example>
        /// <code>
        /// builder.AddRkdScalar()
        ///     .WithHttpLogging(o => o.ExcludedPaths.Add("/health"))
        ///     .WriteHttpLogsToSqlServer(o =>
        ///     {
        ///         o.ConnectionStringName = "Logs";
        ///         o.Table = "logs.HttpRequests";
        ///         o.CreateTable = true;
        ///     });
        /// </code>
        /// </example>
        public static RkdScalarBuilder WriteHttpLogsToSqlServer(
            this RkdScalarBuilder builder,
            Action<SqlServerHttpLogOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var options = new SqlServerHttpLogOptions();
            var section = builder.Configuration.GetSection(SectionName);

            if (section.Exists())
                section.Bind(options);

            configure?.Invoke(options);

            var connectionString = !string.IsNullOrWhiteSpace(options.ConnectionString)
                ? options.ConnectionString
                : !string.IsNullOrWhiteSpace(options.ConnectionStringName)
                    ? builder.Configuration.GetConnectionString(options.ConnectionStringName)
                    : null;

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "WriteHttpLogsToSqlServer: set ConnectionString, or ConnectionStringName with an entry in ConnectionStrings " +
                    $"(in code or in the '{SectionName}' section).");

            if (options.CommandTimeoutSeconds < 0)
                throw new InvalidOperationException("WriteHttpLogsToSqlServer: 'CommandTimeoutSeconds' cannot be negative.");

            SqlTableName.Parse(options.Table);

            return builder.WithHttpLogSink(services => new SqlServerHttpLogSink(
                connectionString, options, services.GetRequiredService<ILogger<SqlServerHttpLogSink>>()));
        }

        /// <summary>
        /// Writes the HTTP logs to SQL Server, in <paramref name="table"/> (<c>dbo.HttpLogs</c> by default).
        /// </summary>
        /// <param name="builder">The Rkd.Scalar builder.</param>
        /// <param name="connectionString">SQL Server connection string.</param>
        /// <param name="table">Table, optionally with schema.</param>
        /// <param name="createTable">Creates the table on the first write when it does not exist.</param>
        /// <returns>The same builder.</returns>
        public static RkdScalarBuilder WriteHttpLogsToSqlServer(
            this RkdScalarBuilder builder,
            string connectionString,
            string table = "dbo.HttpLogs",
            bool createTable = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            return builder.WriteHttpLogsToSqlServer(o =>
            {
                o.ConnectionString = connectionString;
                o.Table = table;
                o.CreateTable = createTable;
            });
        }
    }
}
