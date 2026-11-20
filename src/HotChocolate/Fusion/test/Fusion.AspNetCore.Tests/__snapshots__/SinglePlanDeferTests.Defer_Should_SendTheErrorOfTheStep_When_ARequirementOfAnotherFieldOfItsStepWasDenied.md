# Defer_Should_SendTheErrorOfTheStep_When_ARequirementOfAnotherFieldOfItsStepWasDenied

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","cA"]},{"message":"B failed","path":["user","cC"]}],"data":{"cA":null,"cC":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
