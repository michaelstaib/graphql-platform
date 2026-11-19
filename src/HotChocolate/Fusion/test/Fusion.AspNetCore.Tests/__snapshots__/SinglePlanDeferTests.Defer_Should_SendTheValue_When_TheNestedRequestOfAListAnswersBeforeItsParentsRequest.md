# Defer_Should_SendTheValue_When_TheNestedRequestOfAListAnswersBeforeItsParentsRequest

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"a"},{"id":"1","path":["products",1],"label":"a"},{"id":"2","path":["products",2],"label":"a"},{"id":"3","path":["products",0],"label":"b"},{"id":"4","path":["products",1],"label":"b"},{"id":"5","path":["products",2],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"6","path":["products",0],"label":"x"},{"id":"7","path":["products",1],"label":"x"},{"id":"8","path":["products",2],"label":"x"}],"incremental":[{"id":"3","data":{"author":{"bio":"s"}}},{"id":"4","data":{"author":{"bio":"s"}}},{"id":"5","data":{"author":{"bio":"s"}}},{"id":"6","subPath":["author"],"data":{"age":0}},{"id":"7","subPath":["author"],"data":{"age":0}},{"id":"8","subPath":["author"],"data":{"age":0}}],"completed":[{"id":"3"},{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["author"],"data":{"id":"s"}},{"id":"1","subPath":["author"],"data":{"id":"s"}},{"id":"2","subPath":["author"],"data":{"id":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=3
plain twin: A=1, B=1
```
