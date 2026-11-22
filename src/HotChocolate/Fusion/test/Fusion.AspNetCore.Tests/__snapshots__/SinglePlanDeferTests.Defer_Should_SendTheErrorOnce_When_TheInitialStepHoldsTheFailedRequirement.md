# Defer_Should_SendTheErrorOnce_When_TheInitialStepHoldsTheFailedRequirement

## Payload 1

```json
{"errors":[{"message":"B failed","path":["user","inner"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
