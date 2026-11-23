# Defer_Should_FailTheFragmentWithTheSourceError_When_AnotherFragmentHoldsTheReportedObject

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"],"label":"q"},{"id":"1","path":["user"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"B failed","path":["user","nnA"]}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","inner"]}],"data":{"inner":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
