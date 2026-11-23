# Defer_Should_CompleteTheNestedFragmentWithTheError_When_ItsRequestAnswersAnEntityError

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product"],"label":"x"}],"incremental":[{"id":"1","data":{"author":{"bio":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"1"},{"id":"2","errors":[{"message":"The entity failed.","path":["product","author","age"]}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["author"],"data":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=3
```
