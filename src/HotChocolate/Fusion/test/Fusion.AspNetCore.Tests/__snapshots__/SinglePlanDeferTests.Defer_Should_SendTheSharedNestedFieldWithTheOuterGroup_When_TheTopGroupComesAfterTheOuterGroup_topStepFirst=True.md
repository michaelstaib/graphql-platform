# Defer_Should_SendTheSharedNestedFieldWithTheOuterGroup_When_TheTopGroupComesAfterTheOuterGroup

## Payload 1

```json
{"data":{"products":[{"id":"s"},{"id":"s"},{"id":"s"}]},"pending":[{"id":"0","path":["products"],"label":"outer"},{"id":"2","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"2","subPath":["products",0],"data":{"reviews":[{"rating":0},{"rating":0},{"rating":0}]}},{"id":"2","subPath":["products",1],"data":{"reviews":[{"rating":0},{"rating":0},{"rating":0}]}},{"id":"2","subPath":["products",2],"data":{"reviews":[{"rating":0},{"rating":0},{"rating":0}]}}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"1","path":["products"],"label":"inner"}],"incremental":[{"id":"0","subPath":[0,"reviews",0],"data":{"id":"s"}},{"id":"0","subPath":[0,"reviews",1],"data":{"id":"s"}},{"id":"0","subPath":[0,"reviews",2],"data":{"id":"s"}},{"id":"0","subPath":[1,"reviews",0],"data":{"id":"s"}},{"id":"0","subPath":[1,"reviews",1],"data":{"id":"s"}},{"id":"0","subPath":[1,"reviews",2],"data":{"id":"s"}},{"id":"0","subPath":[2,"reviews",0],"data":{"id":"s"}},{"id":"0","subPath":[2,"reviews",1],"data":{"id":"s"}},{"id":"0","subPath":[2,"reviews",2],"data":{"id":"s"}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=2, C=2
plain twin: A=1, B=1, C=1
```
