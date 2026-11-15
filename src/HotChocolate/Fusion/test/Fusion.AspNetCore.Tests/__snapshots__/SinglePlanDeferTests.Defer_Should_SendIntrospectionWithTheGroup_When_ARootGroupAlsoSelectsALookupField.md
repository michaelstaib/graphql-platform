# Defer_Should_SendIntrospectionWithTheGroup_When_ARootGroupAlsoSelectsALookupField

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":[],"label":"i"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"__type":{"name":"s"}}},{"id":"0","subPath":["product"],"data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
