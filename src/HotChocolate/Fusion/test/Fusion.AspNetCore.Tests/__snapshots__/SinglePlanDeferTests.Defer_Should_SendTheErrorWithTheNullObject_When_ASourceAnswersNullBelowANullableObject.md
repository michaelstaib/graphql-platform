# Defer_Should_SendTheErrorWithTheNullObject_When_ASourceAnswersNullBelowANullableObject

## Payload 1

```json
{"data":{"product":{"name":"s","shipNullable":0}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","maybePack","h2"],"extensions":{"code":"HC0018"}}],"data":{"maybePack":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1, D=1
plain twin: A=1, B=1, C=1, D=1
```
