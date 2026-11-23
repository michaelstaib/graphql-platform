# Defer_Should_FailTheFragmentWithTheError_When_TheNonNullObjectOfTheRequirementIsNull

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","innerNn"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
