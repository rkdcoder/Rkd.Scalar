using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Npgsql;
using NpgsqlTypes;
using Rkd.Scalar.HttpLogging.Postgres;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public class PostgresHttpLogTests
    {
        /// <summary>
        /// Connection string of a PostgreSQL used by the integration tests, e.g.
        /// <c>Host=localhost;Port=5433;Username=postgres;Password=...;Database=postgres</c>.
        /// The integration tests are skipped when it is not set (CI has no PostgreSQL).
        /// </summary>
        private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RKD_POSTGRES_TESTS");

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData("http_logs", "\"public\".\"http_logs\"")]
        [InlineData("logs.HttpRequests", "\"logs\".\"httprequests\"")]
        [InlineData("\"Logs\".\"Http_Requests\"", "\"Logs\".\"Http_Requests\"")]
        public void TableName_ShouldBeParsedAndQuoted(string input, string quoted) =>
            PostgresTableName.Parse(input).Quoted.Should().Be(quoted);

        [Theory]
        [InlineData("")]
        [InlineData("a.b.c")]
        [InlineData("logs.http;DROP TABLE x")]
        [InlineData("logs.\"http\"\"x\"")]
        [InlineData("1logs")]
        public void TableName_ShouldRejectInvalidNames(string input)
        {
            var act = () => PostgresTableName.Parse(input);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CreateScript_ShouldBeIdempotentWithIndexes()
        {
            var script = PostgresHttpLogTable.CreateScript("logs.http_requests");

            script.Should().Contain("CREATE SCHEMA IF NOT EXISTS \"logs\";");
            script.Should().Contain("CREATE TABLE IF NOT EXISTS \"logs\".\"http_requests\"");
            script.Should().Contain("\"trace_id\" text NOT NULL");
            script.Should().Contain("\"request_headers\" jsonb NULL");
            script.Should().Contain("CREATE INDEX IF NOT EXISTS \"ix_logs_http_requests_trace_id\"");
        }

        [Fact]
        public void CreateScript_ShouldNotCreateThePublicSchema() =>
            PostgresHttpLogTable.CreateScript().Should().NotContain("CREATE SCHEMA");

        [Fact]
        public void Prepare_ShouldCutOversizedValuesAndRemoveNulCharacters()
        {
            var entry = new HttpLogEntry
            {
                TraceId = "t",
                Method = "GET",
                Path = "/" + new string('p', 5000),
                RequestBody = "a\0b",
                RequestHeaders = { ["Accept"] = "*/*", ["X-Bad"] = "a\0b" },
                Properties = { ["literal"] = @"\u0000" }
            };

            object? Value(string column, int? max = null) =>
                PostgresHttpLogSink.Prepare(
                    new PostgresHttpLogSink.Target(PostgresHttpLogTable.Columns.Single(c => c.Name == column), NpgsqlDbType.Text, max), entry);

            ((string)Value("path", 2048)!).Should().HaveLength(2048);
            Value("request_body").Should().Be("ab");
            Value("request_headers").Should().Be("""{"Accept":"*/*","X-Bad":"ab"}""");
            Value("properties").Should().Be("""{"literal":"\\u0000"}""");
            Value("request_content_type").Should().BeNull();
        }

        [Fact]
        public void MissingConnectionString_ShouldFailAtStartup()
        {
            var act = () => TestApp.StartAsync(scalar => scalar.WriteHttpLogsToPostgres()).GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*");
        }

        [Fact]
        public async Task Postgres_ShouldCreateTheTableAndWriteBatches()
        {
            Assert.SkipUnless(ConnectionString is not null, "Set RKD_POSTGRES_TESTS to run the PostgreSQL integration tests.");

            var table = $"rkdtests.http_logs_{Guid.NewGuid():N}";

            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithProblemDetails()
                    .WithHttpLogging(o => o.Enrich = (_, entry) => entry.Properties["tenant"] = "acme")
                    .WriteHttpLogsToPostgres(ConnectionString!, table, createTable: true),
                app =>
                {
                    app.MapGet("/items/{id:int}", (int id) => new { id });
                    app.MapGet("/conflict", string () => throw RkdError.Conflict("ITEM_EXISTS", "Já existe."));
                });

            for (var i = 0; i < 20; i++)
                (await app.Client.GetAsync($"/items/{i}?token=abc", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var conflict = await app.Client.GetAsync("/conflict", Ct);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(Ct);

            var rows = 0;

            for (var attempt = 0; attempt < 100 && rows < 21; attempt++)
            {
                await Task.Delay(100, Ct);
                rows = await CountAsync(connection, table);
            }

            rows.Should().Be(21);

            var quoted = PostgresTableName.Parse(table).Quoted;

            await using var command = new NpgsqlCommand(
                $"SELECT status_code, error_code, query_string, properties->>'tenant', response_body, route_pattern, started_at FROM {quoted} WHERE path = '/conflict'",
                connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue();

            reader.GetInt32(0).Should().Be(409);
            reader.GetString(1).Should().Be("ITEM_EXISTS");
            reader.IsDBNull(2).Should().BeTrue();
            reader.GetString(3).Should().Be("acme");
            reader.GetString(4).Should().Contain("Já existe.");
            reader.GetString(5).Should().Be("/conflict");
            reader.GetFieldValue<DateTime>(6).Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
            await reader.DisposeAsync();

            await using var query = new NpgsqlCommand($"SELECT query_string FROM {quoted} WHERE path = '/items/1'", connection);
            ((string?)await query.ExecuteScalarAsync(Ct)).Should().Be("?token=[REDACTED]");

            await ExecuteAsync(connection, $"DROP TABLE {quoted}");
        }

        [Fact]
        public async Task Postgres_TableWithFewerColumns_ShouldStillBeWritten()
        {
            Assert.SkipUnless(ConnectionString is not null, "Set RKD_POSTGRES_TESTS to run the PostgreSQL integration tests.");

            var table = $"rkdtests.slim_{Guid.NewGuid():N}";
            var quoted = PostgresTableName.Parse(table).Quoted;

            await using (var setup = new NpgsqlConnection(ConnectionString))
            {
                await setup.OpenAsync(Ct);
                await ExecuteAsync(setup, "CREATE SCHEMA IF NOT EXISTS rkdtests");
                // path is varchar(10) (cut to fit), method has an incompatible type (ignored), logged_at has a default.
                await ExecuteAsync(setup,
                    $"CREATE TABLE {quoted} (id serial PRIMARY KEY, trace_id varchar(128) NOT NULL, path varchar(10) NOT NULL, " +
                    "status_code integer NOT NULL, method inet NULL, request_headers json NULL, logged_at timestamptz NOT NULL DEFAULT now())");
            }

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WriteHttpLogsToPostgres(ConnectionString!, table),
                app => app.MapGet("/a-very-long-path", () => "pong"));

            await app.Client.GetAsync("/a-very-long-path", Ct);

            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(Ct);

            var rows = 0;

            for (var attempt = 0; attempt < 100 && rows == 0; attempt++)
            {
                await Task.Delay(100, Ct);
                rows = await CountAsync(connection, table);
            }

            rows.Should().Be(1);

            await using var query = new NpgsqlCommand($"SELECT path FROM {quoted}", connection);
            ((string?)await query.ExecuteScalarAsync(Ct)).Should().Be("/a-very-lo");

            await ExecuteAsync(connection, $"DROP TABLE {quoted}");
        }

        [Fact]
        public async Task Postgres_Unavailable_ShouldNotAffectTheApi()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WriteHttpLogsToPostgres(
                    "Host=127.0.0.1;Port=1;Username=x;Password=x;Timeout=1", "public.http_logs"),
                app => app.MapGet("/ping", () => "pong"));

            var started = DateTime.UtcNow;

            for (var i = 0; i < 5; i++)
                (await app.Client.GetStringAsync("/ping", Ct)).Should().Be("pong");

            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "requests never wait for the database");
        }

        private static async Task<int> CountAsync(NpgsqlConnection connection, string table)
        {
            var quoted = PostgresTableName.Parse(table).Quoted;

            await using var exists = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
            exists.Parameters.AddWithValue("name", quoted);

            if (!(bool)(await exists.ExecuteScalarAsync())!)
                return 0;

            await using var command = new NpgsqlCommand($"SELECT count(*)::int FROM {quoted}", connection);

            return (int)(await command.ExecuteScalarAsync())!;
        }

        private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
