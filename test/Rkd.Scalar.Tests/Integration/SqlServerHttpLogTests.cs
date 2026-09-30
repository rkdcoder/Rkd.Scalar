using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Rkd.Scalar.HttpLogging.SqlServer;
using Rkd.Scalar.Tests.Helpers;

namespace Rkd.Scalar.Tests.Integration
{
    public class SqlServerHttpLogTests
    {
        /// <summary>
        /// Connection string of a SQL Server used by the integration tests, e.g.
        /// <c>Server=localhost,14333;User Id=sa;Password=...;TrustServerCertificate=true</c>.
        /// The integration tests are skipped when it is not set (CI has no SQL Server).
        /// </summary>
        private static readonly string? ConnectionString = Environment.GetEnvironmentVariable("RKD_SQLSERVER_TESTS");

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData("HttpLogs", "[dbo].[HttpLogs]")]
        [InlineData("logs.HttpRequests", "[logs].[HttpRequests]")]
        [InlineData("[logs].[Http_Requests]", "[logs].[Http_Requests]")]
        public void TableName_ShouldBeParsedAndQuoted(string input, string quoted) =>
            SqlTableName.Parse(input).Quoted.Should().Be(quoted);

        [Theory]
        [InlineData("")]
        [InlineData("a.b.c")]
        [InlineData("logs.Http;DROP TABLE x")]
        [InlineData("logs.[Http]]x]")]
        public void TableName_ShouldRejectInvalidNames(string input)
        {
            var act = () => SqlTableName.Parse(input);
            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CreateScript_ShouldBeIdempotentWithIndexes()
        {
            var script = SqlServerHttpLogTable.CreateScript("logs.HttpRequests");

            script.Should().Contain("IF SCHEMA_ID(N'logs') IS NULL");
            script.Should().Contain("IF OBJECT_ID(N'[logs].[HttpRequests]', N'U') IS NULL");
            script.Should().Contain("[TraceId] NVARCHAR(128) NOT NULL");
            script.Should().Contain("[RequestBody] NVARCHAR(MAX) NULL");
            script.Should().Contain("CREATE INDEX [IX_logs_HttpRequests_TraceId]");
        }

        [Fact]
        public void DataTable_ShouldTruncateOversizedValues()
        {
            var entry = new HttpLogEntry
            {
                TraceId = "t",
                Method = "GET",
                Path = "/" + new string('p', 5000),
                RequestHeaders = { ["Accept"] = "*/*" }
            };

            using var table = SqlServerHttpLogSink.ToDataTable([entry], SqlServerHttpLogTable.Columns);

            ((string)table.Rows[0]["Path"]).Should().HaveLength(2048);
            ((string)table.Rows[0]["RequestHeaders"]).Should().Be("""{"Accept":"*/*"}""");
            table.Rows[0]["Properties"].Should().Be(DBNull.Value);
        }

        [Fact]
        public void MissingConnectionString_ShouldFailAtStartup()
        {
            var act = () => TestApp.StartAsync(scalar => scalar.WriteHttpLogsToSqlServer()).GetAwaiter().GetResult();

            act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionString*");
        }

        [Fact]
        public async Task SqlServer_ShouldCreateTheTableAndWriteBatches()
        {
            Assert.SkipUnless(ConnectionString is not null, "Set RKD_SQLSERVER_TESTS to run the SQL Server integration tests.");

            var table = $"rkdtests.HttpLogs_{Guid.NewGuid():N}";

            await using var app = await TestApp.StartAsync(
                scalar => scalar
                    .WithProblemDetails()
                    .WithHttpLogging(o => o.Enrich = (_, entry) => entry.Properties["tenant"] = "acme")
                    .WriteHttpLogsToSqlServer(ConnectionString!, table, createTable: true),
                app =>
                {
                    app.MapGet("/items/{id:int}", (int id) => new { id });
                    app.MapGet("/conflict", string () => throw RkdError.Conflict("ITEM_EXISTS", "Já existe."));
                });

            for (var i = 0; i < 20; i++)
                (await app.Client.GetAsync($"/items/{i}?token=abc", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

            var conflict = await app.Client.GetAsync("/conflict", Ct);
            conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(Ct);

            var rows = 0;

            for (var attempt = 0; attempt < 100 && rows < 21; attempt++)
            {
                await Task.Delay(100, Ct);
                rows = await CountAsync(connection, table);
            }

            rows.Should().Be(21);

            await using var command = new SqlCommand(
                $"SELECT StatusCode, ErrorCode, QueryString, Properties, ResponseBody, RoutePattern FROM {SqlTableName.Parse(table).Quoted} WHERE Path = '/conflict'",
                connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).Should().BeTrue();

            reader.GetInt32(0).Should().Be(409);
            reader.GetString(1).Should().Be("ITEM_EXISTS");
            reader.IsDBNull(2).Should().BeTrue();
            reader.GetString(3).Should().Be("""{"tenant":"acme"}""");
            reader.GetString(4).Should().Contain("Já existe.");
            reader.GetString(5).Should().Be("/conflict");
            await reader.DisposeAsync();

            await using var query = new SqlCommand(
                $"SELECT TOP 1 QueryString FROM {SqlTableName.Parse(table).Quoted} WHERE Path = '/items/1'", connection);
            ((string?)await query.ExecuteScalarAsync(Ct)).Should().Be("?token=[REDACTED]");

            await DropAsync(connection, table);
        }

        [Fact]
        public async Task SqlServer_TableWithFewerColumns_ShouldStillBeWritten()
        {
            Assert.SkipUnless(ConnectionString is not null, "Set RKD_SQLSERVER_TESTS to run the SQL Server integration tests.");

            var table = $"rkdtests.Slim_{Guid.NewGuid():N}";
            var quoted = SqlTableName.Parse(table).Quoted;

            await using (var setup = new SqlConnection(ConnectionString))
            {
                await setup.OpenAsync(Ct);
                await ExecuteAsync(setup, "IF SCHEMA_ID(N'rkdtests') IS NULL EXEC(N'CREATE SCHEMA [rkdtests]');");
                await ExecuteAsync(setup,
                    $"CREATE TABLE {quoted} (Id INT IDENTITY PRIMARY KEY, TraceId NVARCHAR(128) NOT NULL, Path NVARCHAR(2048) NOT NULL, " +
                    "StatusCode INT NOT NULL, LoggedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME())");
            }

            await using var app = await TestApp.StartAsync(
                scalar => scalar.WriteHttpLogsToSqlServer(ConnectionString!, table),
                app => app.MapGet("/ping", () => "pong"));

            await app.Client.GetAsync("/ping", Ct);

            await using var connection = new SqlConnection(ConnectionString);
            await connection.OpenAsync(Ct);

            var rows = 0;

            for (var attempt = 0; attempt < 100 && rows == 0; attempt++)
            {
                await Task.Delay(100, Ct);
                rows = await CountAsync(connection, table);
            }

            rows.Should().Be(1);
            await DropAsync(connection, table);
        }

        [Fact]
        public async Task SqlServer_Unavailable_ShouldNotAffectTheApi()
        {
            await using var app = await TestApp.StartAsync(
                scalar => scalar.WriteHttpLogsToSqlServer(
                    "Server=127.0.0.1,1;User Id=sa;Password=x;Connect Timeout=1;TrustServerCertificate=true", "dbo.HttpLogs"),
                app => app.MapGet("/ping", () => "pong"));

            var started = DateTime.UtcNow;

            for (var i = 0; i < 5; i++)
                (await app.Client.GetStringAsync("/ping", Ct)).Should().Be("pong");

            (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(1), "requests never wait for the database");
        }

        private static async Task<int> CountAsync(SqlConnection connection, string table)
        {
            await using var command = new SqlCommand(
                $"IF OBJECT_ID(N'{SqlTableName.Parse(table).Quoted}', N'U') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM {SqlTableName.Parse(table).Quoted}",
                connection);

            return (int)(await command.ExecuteScalarAsync())!;
        }

        private static async Task DropAsync(SqlConnection connection, string table) =>
            await ExecuteAsync(connection, $"DROP TABLE {SqlTableName.Parse(table).Quoted}");

        private static async Task ExecuteAsync(SqlConnection connection, string sql)
        {
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
