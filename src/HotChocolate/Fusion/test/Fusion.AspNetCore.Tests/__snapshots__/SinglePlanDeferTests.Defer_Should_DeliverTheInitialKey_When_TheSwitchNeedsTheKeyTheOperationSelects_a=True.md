# Defer_Should_DeliverTheInitialKey_When_TheSwitchNeedsTheKeyTheOperationSelects

## Payload 1

```json
{"data":{"product":{"upc":"u1","id":"id-u1"}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":1085}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1, S=1
plain twin: A=2, B=1, S=1
```
