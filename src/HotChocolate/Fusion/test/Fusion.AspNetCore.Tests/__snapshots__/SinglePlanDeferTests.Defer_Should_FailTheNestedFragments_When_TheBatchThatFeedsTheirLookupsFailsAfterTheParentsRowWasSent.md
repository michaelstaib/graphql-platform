# Defer_Should_FailTheNestedFragments_When_TheBatchThatFeedsTheirLookupsFailsAfterTheParentsRowWasSent

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="},"topProduct":{"name":"Product: UHJvZHVjdDo4OTAzMzU2Njc="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"]},{"id":"2","path":["topProduct"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"3","path":["product","author"],"label":"bio"},{"id":"4","path":["product","author"],"label":"nick"}],"completed":[{"id":"1"},{"id":"2","errors":[{"message":"Unexpected Execution Error","path":["topProduct","author"]}]},{"id":"3","errors":[{"message":"Unexpected Execution Error"}]},{"id":"4","errors":[{"message":"Unexpected Execution Error"}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
