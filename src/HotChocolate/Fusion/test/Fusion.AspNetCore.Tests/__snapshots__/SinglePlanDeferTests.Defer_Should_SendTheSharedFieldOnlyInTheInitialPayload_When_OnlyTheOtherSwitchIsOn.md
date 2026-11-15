# Defer_Should_SendTheSharedFieldOnlyInTheInitialPayload_When_OnlyTheOtherSwitchIsOn

## Payload 1

```json
{"data":{"product":{"price":0}},"pending":[{"id":"0","path":["product"],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
