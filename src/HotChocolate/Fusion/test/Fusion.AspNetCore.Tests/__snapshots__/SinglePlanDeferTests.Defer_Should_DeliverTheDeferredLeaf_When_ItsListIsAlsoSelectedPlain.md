# Defer_Should_DeliverTheDeferredLeaf_When_ItsListIsAlsoSelectedPlain

## Payload 1

```json
{"data":{"product":{"name":"s","reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["reviews",0],"data":{"stars":0}},{"id":"0","subPath":["reviews",1],"data":{"stars":0}},{"id":"0","subPath":["reviews",2],"data":{"stars":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, C=2
plain twin: A=1, C=1
```
