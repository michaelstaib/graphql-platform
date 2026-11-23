# Defer_Should_SendNullForTheDependentField_When_TheClientDoesNotSelectTheObjectItRequires

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["user","inner"]}],"data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
