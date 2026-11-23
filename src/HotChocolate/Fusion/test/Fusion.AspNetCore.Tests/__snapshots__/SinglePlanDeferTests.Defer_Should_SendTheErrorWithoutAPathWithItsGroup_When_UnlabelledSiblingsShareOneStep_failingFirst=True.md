# Defer_Should_SendTheErrorWithoutAPathWithItsGroup_When_UnlabelledSiblingsShareOneStep

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"rating":123}}],"completed":[{"id":"0","errors":[{"message":"The source failed."},{"message":"Unexpected Execution Error","path":["product","price"]}]},{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
