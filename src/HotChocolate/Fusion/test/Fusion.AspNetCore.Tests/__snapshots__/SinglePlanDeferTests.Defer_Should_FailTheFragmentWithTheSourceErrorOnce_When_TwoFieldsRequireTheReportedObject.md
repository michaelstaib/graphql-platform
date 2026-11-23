# Defer_Should_FailTheFragmentWithTheSourceErrorOnce_When_TwoFieldsRequireTheReportedObject

## Payload 1

```json
{"errors":[{"message":"B failed","path":["user","inner"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
