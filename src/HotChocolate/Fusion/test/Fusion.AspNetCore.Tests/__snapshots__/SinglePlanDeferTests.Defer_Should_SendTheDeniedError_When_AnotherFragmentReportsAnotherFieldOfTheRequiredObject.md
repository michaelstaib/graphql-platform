# Defer_Should_SendTheDeniedError_When_AnotherFragmentReportsAnotherFieldOfTheRequiredObject

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]},{"id":"1","path":["user"],"label":"q"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","errors":[{"message":"B c failed","path":["user","inner","c"]}],"data":{"inner":{"c":null}}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","cA"]}],"data":{"cA":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
