# Defer_Should_FailTheFragmentWithTheErrorOfItsOwnStep_When_TheInitialStepAlsoReportsTheObject

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed 5","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
