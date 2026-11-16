# Defer_Should_AnnounceTheGroupThatHoldsTheField_When_TheInnerSwitchIsConditional

## Payload 1

```json
{"data":{"product":{}},"pending":[{"id":"0","path":["product"],"label":"c"}],"hasNext":true}
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
