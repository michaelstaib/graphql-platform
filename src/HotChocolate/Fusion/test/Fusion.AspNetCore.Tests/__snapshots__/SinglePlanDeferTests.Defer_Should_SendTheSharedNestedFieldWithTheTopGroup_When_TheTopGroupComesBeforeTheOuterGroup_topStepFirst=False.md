# Defer_Should_SendTheSharedNestedFieldWithTheTopGroup_When_TheTopGroupComesBeforeTheOuterGroup

## Payload 1

```json
{"data":{"products":[{"id":"s"},{"id":"s"},{"id":"s"}]},"pending":[{"id":"0","path":["products"],"label":"outer"},{"id":"2","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["products"],"label":"inner"}],"incremental":[{"id":"0","subPath":[0],"data":{"reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"id":"0","subPath":[1],"data":{"reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"id":"0","subPath":[2],"data":{"reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":[0,"reviews",0],"data":{"rating":0}},{"id":"1","subPath":[0,"reviews",1],"data":{"rating":0}},{"id":"1","subPath":[0,"reviews",2],"data":{"rating":0}},{"id":"1","subPath":[1,"reviews",0],"data":{"rating":0}},{"id":"1","subPath":[1,"reviews",1],"data":{"rating":0}},{"id":"1","subPath":[1,"reviews",2],"data":{"rating":0}},{"id":"1","subPath":[2,"reviews",0],"data":{"rating":0}},{"id":"1","subPath":[2,"reviews",1],"data":{"rating":0}},{"id":"1","subPath":[2,"reviews",2],"data":{"rating":0}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=2, C=2
plain twin: A=1, B=1, C=1
```
