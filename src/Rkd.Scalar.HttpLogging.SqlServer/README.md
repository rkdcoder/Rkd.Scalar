# Rkd.Scalar.HttpLogging.SqlServer

SQL Server destination for the HTTP logging of [Rkd.Scalar](https://www.nuget.org/packages/Rkd.Scalar).

```csharp
builder.AddRkdScalar()
    .WithHttpLogging(o => o.ExcludedPaths.Add("/health"))
    .WriteHttpLogsToSqlServer(builder.Configuration.GetConnectionString("Logs")!, "logs.HttpRequests", createTable: true);
```

Or only configuration:

```csharp
builder.AddRkdScalar().WriteHttpLogsToSqlServer();
```

```json
{
  "ConnectionStrings": { "Logs": "Server=...;Database=...;..." },
  "HttpLogging": {
    "ExcludedPaths": [ "/health" ],
    "SqlServer": { "ConnectionStringName": "Logs", "Table": "logs.HttpRequests", "CreateTable": true }
  }
}
```

- Requests are captured by Rkd.Scalar, queued in memory and written in batches with `SqlBulkCopy` by a background
  service: a slow or unavailable database never slows down or breaks the API.
- `CreateTable = true` creates the schema and table on the first write. Without DDL permissions, run the script of
  `SqlServerHttpLogTable.CreateScript("logs.HttpRequests")` once.
- Only the columns your table has are written — drop the ones you do not want (e.g. `RequestHeaders`).
- `TraceId` is indexed: the `traceId` of an error response finds its row.

See the [Rkd.Scalar README](https://github.com/rkdcoder/Rkd.Scalar#http-logging) for the capture options
(bodies, redaction, excluded and sensitive paths, user claims, filters).
