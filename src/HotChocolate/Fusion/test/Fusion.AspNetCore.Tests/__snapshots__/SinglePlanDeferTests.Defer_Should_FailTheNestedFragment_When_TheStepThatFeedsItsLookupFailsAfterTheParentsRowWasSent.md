# Defer_Should_FailTheNestedFragment_When_TheStepThatFeedsItsLookupFailsAfterTheParentsRowWasSent

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"],"label":"g"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":["product","author"],"label":"c"}],"completed":[{"id":"1"},{"id":"2","errors":[{"message":"Unexpected Execution Error"}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
