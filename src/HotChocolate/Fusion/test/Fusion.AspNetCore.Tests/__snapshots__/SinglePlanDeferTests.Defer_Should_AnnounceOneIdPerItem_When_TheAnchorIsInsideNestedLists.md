# Defer_Should_AnnounceOneIdPerItem_When_TheAnchorIsInsideNestedLists

## Payload 1

```json
{"data":{"products":[{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}]},"pending":[{"id":"0","path":["products",0,"reviews",0],"label":"r"},{"id":"1","path":["products",0,"reviews",1],"label":"r"},{"id":"2","path":["products",0,"reviews",2],"label":"r"},{"id":"3","path":["products",1,"reviews",0],"label":"r"},{"id":"4","path":["products",1,"reviews",1],"label":"r"},{"id":"5","path":["products",1,"reviews",2],"label":"r"},{"id":"6","path":["products",2,"reviews",0],"label":"r"},{"id":"7","path":["products",2,"reviews",1],"label":"r"},{"id":"8","path":["products",2,"reviews",2],"label":"r"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}},{"id":"1","data":{"rating":0}},{"id":"2","data":{"rating":0}},{"id":"3","data":{"rating":0}},{"id":"4","data":{"rating":0}},{"id":"5","data":{"rating":0}},{"id":"6","data":{"rating":0}},{"id":"7","data":{"rating":0}},{"id":"8","data":{"rating":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
