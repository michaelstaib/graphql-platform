# Defer_Should_DeliverBothNestedFragments_When_AnotherSwitchedFragmentSelectsTheAuthorOfAList

## Payload 1

```json
{"data":{"products":[{"name":"s","author":{}},{"name":"s","author":{}},{"name":"s","author":{}}]},"pending":[{"id":"0","path":["products",0,"author"]},{"id":"1","path":["products",1,"author"]},{"id":"2","path":["products",2,"author"]},{"id":"3","path":["products",0,"author"]},{"id":"4","path":["products",1,"author"]},{"id":"5","path":["products",2,"author"]},{"id":"6","path":["products",0,"author"]},{"id":"7","path":["products",1,"author"]},{"id":"8","path":["products",2,"author"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"bio":"s"}},{"id":"1","data":{"bio":"s"}},{"id":"2","data":{"bio":"s"}},{"id":"3","data":{"nick":"s"}},{"id":"4","data":{"nick":"s"}},{"id":"5","data":{"nick":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
