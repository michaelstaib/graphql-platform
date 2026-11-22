# Defer_Should_SendTheErrorOnce_When_TheCompletionOfAnotherFragmentReportsTheRequiredField

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="}}},"pending":[{"id":"0","path":["user"]},{"id":"1","path":["user"],"label":"q"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"B failed","path":["user","inner","a"]}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=3
```
