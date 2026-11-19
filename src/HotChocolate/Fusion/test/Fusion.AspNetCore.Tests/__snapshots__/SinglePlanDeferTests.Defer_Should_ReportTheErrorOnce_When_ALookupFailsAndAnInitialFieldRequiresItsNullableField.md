# Defer_Should_ReportTheErrorOnce_When_ALookupFailsAndAnInitialFieldRequiresItsNullableField

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","weight"]}],"data":{"product":{"name":"s","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, W=2
plain twin: A=1, W=1
```
