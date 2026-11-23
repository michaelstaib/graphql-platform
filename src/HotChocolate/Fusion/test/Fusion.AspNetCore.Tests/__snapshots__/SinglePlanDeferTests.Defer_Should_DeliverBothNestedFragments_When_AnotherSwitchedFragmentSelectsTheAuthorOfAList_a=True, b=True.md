# Defer_Should_DeliverBothNestedFragments_When_AnotherSwitchedFragmentSelectsTheAuthorOfAList

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0]},{"id":"1","path":["products",1]},{"id":"2","path":["products",2]},{"id":"3","path":["products",0]},{"id":"4","path":["products",1]},{"id":"5","path":["products",2]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"6","path":["products",0,"author"]},{"id":"7","path":["products",0,"author"]},{"id":"8","path":["products",1,"author"]},{"id":"9","path":["products",1,"author"]},{"id":"10","path":["products",2,"author"]},{"id":"11","path":["products",2,"author"]},{"id":"12","path":["products",0,"author"]},{"id":"13","path":["products",1,"author"]},{"id":"14","path":["products",2,"author"]}],"incremental":[{"id":"0","data":{"author":{}}},{"id":"1","data":{"author":{}}},{"id":"2","data":{"author":{}}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"6","data":{"bio":"s"}},{"id":"7","data":{"nick":"s"}},{"id":"8","data":{"bio":"s"}},{"id":"9","data":{"nick":"s"}},{"id":"10","data":{"bio":"s"}},{"id":"11","data":{"nick":"s"}}],"completed":[{"id":"6"},{"id":"7"},{"id":"8"},{"id":"9"},{"id":"10"},{"id":"11"},{"id":"12"},{"id":"13"},{"id":"14"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
