# Defer_Should_SendEveryItem_When_OneSourceObjectHasSeveralTargets

## Payload 1

```json
{"data":{"products":[{"name":"s","price":0},{"name":"s","price":0},{"name":"s","price":0}],"again":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["again"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":[0],"data":{"price":0}},{"id":"0","subPath":[1],"data":{"price":0}},{"id":"0","subPath":[2],"data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
