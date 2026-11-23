# Defer_Should_FailTheFragmentWithTheSourceError_When_TheStepThatHoldsTheUnselectedObjectFails

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","cA":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
