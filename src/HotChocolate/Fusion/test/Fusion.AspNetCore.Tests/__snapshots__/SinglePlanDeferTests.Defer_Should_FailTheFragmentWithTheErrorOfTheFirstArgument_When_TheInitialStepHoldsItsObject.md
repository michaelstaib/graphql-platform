# Defer_Should_FailTheFragmentWithTheErrorOfTheFirstArgument_When_TheInitialStepHoldsItsObject

## Payload 1

```json
{"errors":[{"message":"B failed","path":["user","inner"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":null,"box":{"y":"Box: Qm94OjUxMjIxMzYxNw=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["user","nnAZ"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1, F=2
```
