# Defer_Should_SendTheItemErrorWithTheFailedFragment_When_AnotherObjectOfTheListDelivers

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"b"},{"id":"1","path":["products",1],"label":"b"},{"id":"2","path":["products",2],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{"body":"Review: UmV2aWV3OjM1NTk1ODcyNTc="},{"body":"Review: UmV2aWV3OjM0MTY1NTY2MTk="},{"body":"Review: UmV2aWV3OjMyMjA1Mjk4NjE="}]}},{"id":"2","data":{"reviews":[{"body":"Review: UmV2aWV3OjIwMDQxMTQ="},{"body":"Review: UmV2aWV3OjMyMDQ5MTkzMDY="},{"body":"Review: UmV2aWV3OjIyODEyMjYxNjU="}]}}],"completed":[{"id":"0"},{"id":"1","errors":[{"message":"C failed 0","path":["products",1,"reviews",0]}]},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, C=1
```
