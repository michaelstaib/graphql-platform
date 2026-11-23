# Defer_Should_KeepOnlyTheInstanceOfTheItemOpen_When_ItsEntityIsMissingAndTheSiblingStillFetches

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"a"},{"id":"1","path":["products",1],"label":"a"},{"id":"2","path":["products",2],"label":"a"},{"id":"3","path":["products",0],"label":"b"},{"id":"4","path":["products",1],"label":"b"},{"id":"5","path":["products",2],"label":"b"},{"id":"6","path":["products",0],"label":"c"},{"id":"7","path":["products",1],"label":"c"},{"id":"8","path":["products",2],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjE4ODExMjgxNzM="}}},{"id":"1","data":{"author":{"id":"QXV0aG9yOjEwMzI1NzQwOTA="}}},{"id":"2","data":{"author":{"id":"QXV0aG9yOjEwNTE2NTA2NDc="}}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"3","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjE4ODExMjgxNzM="}},{"id":"5","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjEwNTE2NTA2NDc="}}],"completed":[{"id":"3"},{"id":"5"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"4","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjEwMzI1NzQwOTA="}}],"completed":[{"id":"4"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
A=1, B=3
```
