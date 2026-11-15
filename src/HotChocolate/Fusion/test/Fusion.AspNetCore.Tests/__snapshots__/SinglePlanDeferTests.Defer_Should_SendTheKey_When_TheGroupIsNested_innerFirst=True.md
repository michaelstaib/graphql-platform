# Defer_Should_SendTheKey_When_TheGroupIsNested

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["product"],"label":"i"}],"incremental":[{"id":"0","data":{"price":0}},{"id":"1","data":{"id":"s","rating":0}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
