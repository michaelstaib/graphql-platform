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
{"pending":[{"id":"4","path":["products",0,"reviews",0],"label":"inner"},{"id":"5","path":["products",0,"reviews",1],"label":"inner"},{"id":"6","path":["products",0,"reviews",2],"label":"inner"},{"id":"7","path":["products",1,"reviews",0],"label":"inner"},{"id":"8","path":["products",1,"reviews",1],"label":"inner"},{"id":"9","path":["products",1,"reviews",2],"label":"inner"},{"id":"10","path":["products",2,"reviews",0],"label":"inner"},{"id":"11","path":["products",2,"reviews",1],"label":"inner"},{"id":"12","path":["products",2,"reviews",2],"label":"inner"}],"incremental":[{"id":"0","subPath":["reviews",0],"data":{"id":"s"}},{"id":"0","subPath":["reviews",1],"data":{"id":"s"}},{"id":"0","subPath":["reviews",2],"data":{"id":"s"}},{"id":"1","subPath":["reviews",0],"data":{"id":"s"}},{"id":"1","subPath":["reviews",1],"data":{"id":"s"}},{"id":"1","subPath":["reviews",2],"data":{"id":"s"}},{"id":"2","subPath":["reviews",0],"data":{"id":"s"}},{"id":"2","subPath":["reviews",1],"data":{"id":"s"}},{"id":"2","subPath":["reviews",2],"data":{"id":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"},{"id":"9"},{"id":"10"},{"id":"11"},{"id":"12"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=2, C=2
plain twin: A=1, B=1, C=1
```
