# Defer_Should_FailTheFragmentWithTheSourceError_When_TheInitialStepHoldsTheReportedList

## Payload 1

```json
{"errors":[{"message":"B failed","path":["user","inners"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inners":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["user","nnL"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
