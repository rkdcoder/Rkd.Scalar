# Rkd.Scalar.HttpLogging.Postgres

PostgreSQL destination for the HTTP logging of [Rkd.Scalar](https://www.nuget.org/packages/Rkd.Scalar).

```csharp
builder.AddRkdScalar()
    .WithHttpLogging(o => o.ExcludedPaths.Add("/health"))
    .WriteHttpLogsToPostgres(builder.Configuration.GetConnectionString("Logs")!, "logs.http_requests", createTable: true);
```

Or only configuration:

```csharp
builder.AddRkdScalar().WriteHttpLogsToPostgres();
```

```json
{
  "ConnectionStrings": { "Logs": "Host=...;Database=...;Username=...;Password=..." },
  "HttpLogging": {
    "ExcludedPaths": [ "/health" ],
    "Postgres": { "ConnectionStringName": "Logs", "Table": "logs.http_requests", "CreateTable": true }
  }
}
```

- Requests are captured by Rkd.Scalar, queued in memory and written in batches with binary `COPY` by a background
  service: a slow or unavailable database never slows down or breaks the API.
- `CreateTable = true` creates the schema and table on the first write. Without DDL permissions, run the script of
  `PostgresHttpLogTable.CreateScript("logs.http_requests")` once.
- Columns are `snake_case` (`trace_id`, `started_at`, `status_code`…); `request_headers` and `properties` are `jsonb`.
- Only the columns your table has (with a compatible type) are written — drop the ones you do not want
  (e.g. `request_headers`). Texts are cut to the size of `varchar(n)` columns.
- Unquoted table names are lower-cased, as PostgreSQL does (`Logs.HttpRequests` is `logs.httprequests`); quote a name
  to keep its case (`"\"Logs\".\"HttpRequests\""`).
- `trace_id` is indexed: the `traceId` of an error response finds its row.

See the [Rkd.Scalar README](https://github.com/rkdcoder/Rkd.Scalar#http-logging) for the capture options
(bodies, redaction, excluded and sensitive paths, user claims, filters).
