# Defer_Should_SendTheErrorWithTheNullField_When_ASourceAnswersNullAndTheErrorModeIsNull

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipNullable":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","maybePack","h2"],"extensions":{"code":"HC0018"}}],"data":{"maybePack":{"h2":null}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, D=2
```
