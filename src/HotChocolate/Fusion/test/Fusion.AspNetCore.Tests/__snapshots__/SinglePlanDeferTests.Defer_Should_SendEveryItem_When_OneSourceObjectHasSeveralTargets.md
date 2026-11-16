# Defer_Should_SendEveryItem_When_OneSourceObjectHasSeveralTargets

## Payload 1

```json
{"data":{"products":[{"name":"s","price":0},{"name":"s","price":0},{"name":"s","price":0}],"again":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["again",0],"label":"d"},{"id":"1","path":["again",1],"label":"d"},{"id":"2","path":["again",2],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}},{"id":"1","data":{"price":0}},{"id":"2","data":{"price":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
