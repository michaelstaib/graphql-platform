# Defer_Should_ReportTheErrorOnce_When_ALookupFailsAndAnInitialFieldRequiresItsRowsAndNullsStay

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","package"]}],"data":{"product":{"name":"s","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"package":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
