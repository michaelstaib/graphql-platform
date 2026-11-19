# Defer_Should_SendNullWithTheStepError_When_TheStepFailsAndLeavesARowWithoutAResult

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"bio":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":["product"],"label":"g"}],"incremental":[{"id":"1","data":{"price":123.456}},{"id":"2","errors":[{"message":"Unexpected Execution Error","path":["product","author","note"]}],"subPath":["author"],"data":{"note":null}}],"completed":[{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
