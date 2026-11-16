# Defer_Should_DeliverTheDeferredField_When_TheOperationIsAMutation

## Payload 1

```json
{"data":{"createProduct":{"name":"s"}},"pending":[{"id":"0","path":["createProduct"],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
