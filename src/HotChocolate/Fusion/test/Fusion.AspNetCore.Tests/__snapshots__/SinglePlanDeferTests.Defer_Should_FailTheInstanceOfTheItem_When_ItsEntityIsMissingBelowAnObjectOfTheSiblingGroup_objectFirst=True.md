# Defer_Should_FailTheInstanceOfTheItem_When_ItsEntityIsMissingBelowAnObjectOfTheSiblingGroup

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"a"},{"id":"1","path":["products",1],"label":"a"},{"id":"2","path":["products",2],"label":"a"},{"id":"3","path":["products",0],"label":"b"},{"id":"4","path":["products",1],"label":"b"},{"id":"5","path":["products",2],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjE4ODExMjgxNzM="}}},{"id":"1","data":{"author":{"id":"QXV0aG9yOjEwMzI1NzQwOTA="}}},{"id":"2","data":{"author":{"id":"QXV0aG9yOjEwNTE2NTA2NDc="}}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"3","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjE4ODExMjgxNzM="}},{"id":"5","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjEwNTE2NTA2NDc="}}],"completed":[{"id":"3"},{"id":"4","errors":[{"message":"Cannot return null for non-nullable field.","path":["products",1,"author","bio"],"extensions":{"code":"HC0018"}}]},{"id":"5"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
