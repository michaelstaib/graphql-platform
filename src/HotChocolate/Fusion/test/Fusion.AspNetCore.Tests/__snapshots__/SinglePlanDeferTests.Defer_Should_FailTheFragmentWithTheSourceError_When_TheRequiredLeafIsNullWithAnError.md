# Defer_Should_FailTheFragmentWithTheSourceError_When_TheRequiredLeafIsNullWithAnError

## Payload 1

```json
{"errors":[{"message":"B failed","path":["user","inner","a"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"a":null,"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
