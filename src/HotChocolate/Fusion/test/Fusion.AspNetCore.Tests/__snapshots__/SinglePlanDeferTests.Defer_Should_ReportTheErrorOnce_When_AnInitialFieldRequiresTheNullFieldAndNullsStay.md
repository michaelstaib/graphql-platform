# Defer_Should_ReportTheErrorOnce_When_AnInitialFieldRequiresTheNullFieldAndNullsStay

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","weight"],"extensions":{"code":"HC0018"}}],"data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2
```
