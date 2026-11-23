# Defer_Should_DeliverTheInitialField_When_TheSwitchAndTheInitialFieldNeedTheSameKey

## Payload 1

```json
{"data":{"product":{"upc":"u1","rating":5}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":1085}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1, S=1, T=1
plain twin: A=2, B=1, S=1, T=1
```
