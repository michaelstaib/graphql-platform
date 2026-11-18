# Defer_Should_SendOneRequest_When_UnlabelledFragmentsSitAtAnAbstractAndAConcreteAnchor

## Payload 1

```json
{"data":{"items":[{"id":"s"},{"id":"s"},{"id":"s"}],"book":{"id":"s"}},"pending":[{"id":"0","path":["items",0]},{"id":"1","path":["items",2]},{"id":"2","path":["book"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}},{"id":"1","data":{"rating":0}},{"id":"2","data":{"rating":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
