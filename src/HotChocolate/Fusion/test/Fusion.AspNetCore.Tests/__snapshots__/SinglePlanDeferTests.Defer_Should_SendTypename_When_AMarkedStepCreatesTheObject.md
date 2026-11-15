# Defer_Should_SendTypename_When_AMarkedStepCreatesTheObject

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["product","reviews"],"label":"i"}],"incremental":[{"id":"0","data":{"reviews":[{},{},{}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":[0],"data":{"__typename":"s","id":"s","rating":0}},{"id":"1","subPath":[1],"data":{"__typename":"s","id":"s","rating":0}},{"id":"1","subPath":[2],"data":{"__typename":"s","id":"s","rating":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
