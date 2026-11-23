# Defer_Should_SendTheErrorWithTheGroupOfTheField_When_UnlabelledSiblingsShareOneStep

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":123}},{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["product","price"]}],"data":{"price":null}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
