# Defer_Should_SendEqualLookupsAsOneRequest_When_OnlyOneOfThemCouldTakeItsKey

## Payload 1

```json
{"data":{"current":{"user":{"code":"s"}}},"pending":[{"id":"0","path":["current","user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"nick":"s","total":0,"friend":{"nick":"s","total":0}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, C=2, D=1, F=1
plain twin: A=1, C=2, D=2, F=1
```
