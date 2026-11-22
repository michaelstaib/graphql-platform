# Defer_Should_FailTheInnerFragmentWithTheSourceError_When_TheOuterFragmentHoldsTheFailedEntity

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["user"],"label":"i"}],"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","inner"]}],"data":{"inner":null}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"B failed","path":["user","nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
