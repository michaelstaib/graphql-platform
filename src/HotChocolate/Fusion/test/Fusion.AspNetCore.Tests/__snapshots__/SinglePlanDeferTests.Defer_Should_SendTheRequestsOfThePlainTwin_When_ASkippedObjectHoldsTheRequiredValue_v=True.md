# Defer_Should_SendTheRequestsOfThePlainTwin_When_ASkippedObjectHoldsTheRequiredValue

## Payload 1

```json
{"data":{"user":{"nick":"s"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","inner"]}],"data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
