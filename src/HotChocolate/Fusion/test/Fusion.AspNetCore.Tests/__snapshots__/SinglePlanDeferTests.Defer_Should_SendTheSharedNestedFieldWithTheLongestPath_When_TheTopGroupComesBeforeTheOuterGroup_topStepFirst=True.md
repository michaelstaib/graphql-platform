# Defer_Should_SendTheSharedNestedFieldWithTheLongestPath_When_TheTopGroupComesBeforeTheOuterGroup

## Payload 1

```json
{"data":{"products":[{"id":"s"},{"id":"s"},{"id":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"outer"},{"id":"1","path":["products",1],"label":"outer"},{"id":"2","path":["products",2],"label":"outer"},{"id":"3","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",0,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",0,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",0,"reviews",2],"data":{"rating":0}},{"id":"1","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",1,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",1,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",1,"reviews",2],"data":{"rating":0}},{"id":"2","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",2,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",2,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",2,"reviews",2],"data":{"rating":0}}],"completed":[{"id":"3"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["reviews",0],"data":{"id":"s"}},{"id":"0","subPath":["reviews",1],"data":{"id":"s"}},{"id":"0","subPath":["reviews",2],"data":{"id":"s"}},{"id":"1","subPath":["reviews",0],"data":{"id":"s"}},{"id":"1","subPath":["reviews",1],"data":{"id":"s"}},{"id":"1","subPath":["reviews",2],"data":{"id":"s"}},{"id":"2","subPath":["reviews",0],"data":{"id":"s"}},{"id":"2","subPath":["reviews",1],"data":{"id":"s"}},{"id":"2","subPath":["reviews",2],"data":{"id":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=2
plain twin: A=1, B=1, C=1
```
