# Defer_Should_SendNullForTheFields_When_AnOperationOfTheBatchHasNoResultAndTheirLookupsAreSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="},"topProduct":{"name":"Product: UHJvZHVjdDo4OTAzMzU2Njc="}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["topProduct"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product","author"],"label":"bio"},{"id":"3","path":["product","author"],"label":"nick"}],"incremental":[{"id":"0","data":{"author":{}}}],"completed":[{"id":"0"},{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["topProduct","author"]}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","data":{"bio":null}},{"id":"3","data":{"nick":null}}],"completed":[{"id":"2"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
