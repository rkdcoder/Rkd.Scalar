using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Rkd.Scalar.HttpLogging.Postgres;

namespace Rkd.Scalar
{
    /// <summary>
    /// Writes the HTTP logs of Rkd.Scalar to PostgreSQL.
    /// </summary>
    public static class PostgresHttpLogBuilderExtensions
    {
        /// <summary>Configuration section of <see cref="PostgresHttpLogOptions"/>.</summary>
        public const string SectionName = "HttpLogging:Postgres";

        /// <summary>
        /// Writes the HTTP logs (<c>WithHttpLogging()</c>, enabled with its defaults when needed) to PostgreSQL, in
        /// batches, from a background service. Options come from the <c>HttpLogging:Postgres</c> section, then
        /// <paramref name="configure"/>.
        /// </summary>
        /// <param name="builder">The Rkd.Scalar builder.</param>
        /// <param name="configure">Connection, table and table creation.</param>
        /// <returns>The same builder.</returns>
        /// <example>
        /// <code>
        /// builder.AddRkdScalar()
        ///     .WithHttpLogging(o => o.ExcludedPaths.Add("/health"))
        ///     .WriteHttpLogsToPostgres(o =>
        ///     {
        ///         o.ConnectionStringName = "Logs";
        ///         o.Table = "logs.http_requests";
        ///         o.CreateTable = true;
        ///     });
        /// </code>
        /// </example>
        public static RkdScalarBuilder WriteHttpLogsToPostgres(
            this RkdScalarBuilder builder,
            Action<PostgresHttpLogOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(builder);

            var options = new PostgresHttpLogOptions();
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
                    "WriteHttpLogsToPostgres: set ConnectionString, or ConnectionStringName with an entry in ConnectionStrings " +
                    $"(in code or in the '{SectionName}' section).");

            if (options.CommandTimeoutSeconds < 0)
                throw new InvalidOperationException("WriteHttpLogsToPostgres: 'CommandTimeoutSeconds' cannot be negative.");

            PostgresTableName.Parse(options.Table);

            return builder.WithHttpLogSink(services => new PostgresHttpLogSink(
                connectionString, options, services.GetRequiredService<ILogger<PostgresHttpLogSink>>()));
        }

        /// <summary>
        /// Writes the HTTP logs to PostgreSQL, in <paramref name="table"/> (<c>public.http_logs</c> by default).
        /// </summary>
        /// <param name="builder">The Rkd.Scalar builder.</param>
        /// <param name="connectionString">PostgreSQL connection string.</param>
        /// <param name="table">Table, optionally with schema.</param>
        /// <param name="createTable">Creates the table on the first write when it does not exist.</param>
        /// <returns>The same builder.</returns>
        public static RkdScalarBuilder WriteHttpLogsToPostgres(
            this RkdScalarBuilder builder,
            string connectionString,
            string table = "public.http_logs",
            bool createTable = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

            return builder.WriteHttpLogsToPostgres(o =>
            {
                o.ConnectionString = connectionString;
                o.Table = table;
                o.CreateTable = createTable;
            });
        }
    }
}
