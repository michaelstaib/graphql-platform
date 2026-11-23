# Defer_Should_SendNoError_When_TheStepOfADroppedFragmentFailsBeforeTheLastFragmentCompletes

## Payload 1

```json
{"data":{"products":[{"id":"s"},{"id":"s"},{"id":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"outer"},{"id":"1","path":["products",1],"label":"outer"},{"id":"2","path":["products",2],"label":"outer"},{"id":"3","path":[],"label":"top"},{"id":"4","path":[],"label":"last"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",0,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",0,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",0,"reviews",2],"data":{"rating":0}},{"id":"1","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",1,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",1,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",1,"reviews",2],"data":{"rating":0}},{"id":"2","data":{"reviews":[{},{},{}]}},{"id":"3","subPath":["products",2,"reviews",0],"data":{"rating":0}},{"id":"3","subPath":["products",2,"reviews",1],"data":{"rating":0}},{"id":"3","subPath":["products",2,"reviews",2],"data":{"rating":0}}],"completed":[{"id":"3"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["reviews",0],"data":{"id":"s"}},{"id":"0","subPath":["reviews",1],"data":{"id":"s"}},{"id":"0","subPath":["reviews",2],"data":{"id":"s"}},{"id":"1","subPath":["reviews",0],"data":{"id":"s"}},{"id":"1","subPath":["reviews",1],"data":{"id":"s"}},{"id":"1","subPath":["reviews",2],"data":{"id":"s"}},{"id":"2","subPath":["reviews",0],"data":{"id":"s"}},{"id":"2","subPath":["reviews",1],"data":{"id":"s"}},{"id":"2","subPath":["reviews",2],"data":{"id":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"4","subPath":["products",0],"data":{"title":"s"}},{"id":"4","subPath":["products",1],"data":{"title":"s"}},{"id":"4","subPath":["products",2],"data":{"title":"s"}}],"completed":[{"id":"4"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=2, D=1
```
