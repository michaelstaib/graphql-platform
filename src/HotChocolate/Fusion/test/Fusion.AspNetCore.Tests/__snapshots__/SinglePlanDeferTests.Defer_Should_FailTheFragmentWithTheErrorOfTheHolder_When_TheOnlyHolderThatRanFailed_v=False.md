# Defer_Should_FailTheFragmentWithTheErrorOfTheHolder_When_TheOnlyHolderThatRanFailed

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["user"]}],"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","inner"]}],"data":{"inner":null}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
