# Defer_Should_FailOnlyTheInstanceOfTheNullKeyItem_When_TheParentStepAnswersNullForTheKeyOfOneItem

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"d"},{"id":"1","path":["products",1],"label":"d"},{"id":"2","path":["products",2],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"id":"UHJvZHVjdDozOTM1NzEyMDU5","price":123.456}},{"id":"2","data":{"id":"UHJvZHVjdDoyMjE1MjQ2OTE2","price":123.456}}],"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["products",0,"id"],"extensions":{"code":"HC0018"}}]},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
