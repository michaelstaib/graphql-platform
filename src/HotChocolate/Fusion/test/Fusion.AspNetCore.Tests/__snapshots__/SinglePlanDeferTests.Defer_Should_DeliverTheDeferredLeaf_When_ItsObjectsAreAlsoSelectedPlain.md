# Defer_Should_DeliverTheDeferredLeaf_When_ItsObjectsAreAlsoSelectedPlain

## Payload 1

```json
{"data":{"product":{"name":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["reviews",0],"data":{"body":"s"}},{"id":"0","subPath":["reviews",1],"data":{"body":"s"}},{"id":"0","subPath":["reviews",2],"data":{"body":"s"}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
