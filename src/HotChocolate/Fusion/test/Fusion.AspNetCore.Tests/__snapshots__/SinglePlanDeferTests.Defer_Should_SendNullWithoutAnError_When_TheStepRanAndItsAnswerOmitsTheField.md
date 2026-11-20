# Defer_Should_SendNullWithoutAnError_When_TheStepRanAndItsAnswerOmitsTheField

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","cA"]}],"data":{"cA":null,"dC":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, D=1
```
