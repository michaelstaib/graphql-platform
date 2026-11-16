# Defer_Should_AnnounceTheNestedGroupWithTheFirstPayload_When_TheOuterGroupIsOnlyIntrospection

## Payload 1

```json
{"data":{"__type":{"name":"s"}},"pending":[{"id":"0","path":[],"label":"i"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"product":{"name":"s"}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1
plain twin: A=1
```
