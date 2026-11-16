# Defer_Should_NullTheNullableItem_When_ARequiredFieldOfTheItemFailsInsideTheGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"r"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","reviews",0,"body"]},{"message":"Unexpected Execution Error","path":["product","reviews",1,"body"]},{"message":"Unexpected Execution Error","path":["product","reviews",2,"body"]}],"data":{"reviews":[null,null,null]}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, C=1
```
