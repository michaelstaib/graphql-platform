# Defer_Should_MatchThePlainTwin_When_TheAnchorIsInsideAList

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"price"},{"id":"1","path":["products",1],"label":"price"},{"id":"2","path":["products",2],"label":"price"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}},{"id":"1","data":{"price":0}},{"id":"2","data":{"price":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
