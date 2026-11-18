# Defer_Should_SendTheEntityErrorOnce_When_UnlabelledSiblingsShareTheStepAndOneFailsOnAnotherField

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"]},{"id":"2","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","errors":[{"message":"The entity failed.","path":["product","author","note"]}],"subPath":["author"],"data":{"note":null}}],"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
