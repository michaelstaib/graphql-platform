# Defer_Should_FailTheFragmentWithTheErrorOfTheHolder_When_OneStepHoldsTheValueForBothValues

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["user","inner"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
