# Defer_Should_MatchThePlainTwin_When_TheAnchorIsInsideAList

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products"],"label":"price"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":[0],"data":{"price":0}},{"id":"0","subPath":[1],"data":{"price":0}},{"id":"0","subPath":[2],"data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
