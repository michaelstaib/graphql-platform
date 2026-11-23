# Defer_Should_SendTheRequestsOfThePlainTwin_When_UnlabelledSiblingsFetchTheFieldForOwnSwitches

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
