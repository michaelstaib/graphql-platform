# Defer_Should_DeliverTheProducts_When_TheTargetPathNamesNestedInterfaces

## Payload 1

```json
{"data":{"orders":[{"items":[{"product":{"id":"s"}},{"product":{"id":"s"}},{"product":{"id":"s"}}]},{"items":[{"product":{"id":"s"}},{"product":{"id":"s"}},{"product":{"id":"s"}}]},{"items":[{"product":{"id":"s"}},{"product":{"id":"s"}},{"product":{"id":"s"}}]}]},"pending":[{"id":"0","path":["orders",0,"items",0,"product"]},{"id":"1","path":["orders",0,"items",1,"product"]},{"id":"2","path":["orders",0,"items",2,"product"]},{"id":"3","path":["orders",1,"items",0,"product"]},{"id":"4","path":["orders",1,"items",1,"product"]},{"id":"5","path":["orders",1,"items",2,"product"]},{"id":"6","path":["orders",2,"items",0,"product"]},{"id":"7","path":["orders",2,"items",1,"product"]},{"id":"8","path":["orders",2,"items",2,"product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"name":"s","description":"s"}},{"id":"1","data":{"name":"s","description":"s"}},{"id":"2","data":{"name":"s","description":"s"}},{"id":"3","data":{"name":"s","description":"s"}},{"id":"4","data":{"name":"s","description":"s"}},{"id":"5","data":{"name":"s","description":"s"}},{"id":"6","data":{"name":"s","description":"s"}},{"id":"7","data":{"name":"s","description":"s"}},{"id":"8","data":{"name":"s","description":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
deferred: ordering=1, products=1
plain twin: ordering=1, products=1
```
