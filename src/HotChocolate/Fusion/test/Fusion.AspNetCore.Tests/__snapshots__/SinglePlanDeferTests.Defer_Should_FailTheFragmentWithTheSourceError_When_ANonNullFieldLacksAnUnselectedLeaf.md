# Defer_Should_FailTheFragmentWithTheSourceError_When_ANonNullFieldLacksAnUnselectedLeaf

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["user","nnX"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
