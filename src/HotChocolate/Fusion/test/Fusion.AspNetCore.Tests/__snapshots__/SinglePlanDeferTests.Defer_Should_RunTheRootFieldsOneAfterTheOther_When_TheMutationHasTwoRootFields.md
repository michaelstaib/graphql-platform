# Defer_Should_RunTheRootFieldsOneAfterTheOther_When_TheMutationHasTwoRootFields

## Payload 1

```json
{"data":{"first":{"name":"s"},"second":{"name":"s"}},"pending":[{"id":"0","path":["first"],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2, B=1
```
