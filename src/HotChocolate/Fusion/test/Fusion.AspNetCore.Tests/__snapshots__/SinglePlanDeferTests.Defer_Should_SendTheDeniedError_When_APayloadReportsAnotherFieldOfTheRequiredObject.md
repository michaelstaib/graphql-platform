# Defer_Should_SendTheDeniedError_When_APayloadReportsAnotherFieldOfTheRequiredObject

## Payload 1

```json
{"errors":[{"message":"B c failed","path":["user","inner","c"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":null}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","cA"]}],"data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
