# Defer_Should_SendTheKey_When_AnotherGroupFetchesThroughIt

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"d"},{"id":"1","path":["product"],"label":"e"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"id":"s"}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"price":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
