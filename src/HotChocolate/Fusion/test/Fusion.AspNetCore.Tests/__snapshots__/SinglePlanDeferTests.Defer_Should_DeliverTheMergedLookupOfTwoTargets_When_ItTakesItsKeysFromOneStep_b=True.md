# Defer_Should_DeliverTheMergedLookupOfTwoTargets_When_ItTakesItsKeysFromOneStep

## Payload 1

```json
{"data":{"product":{"name":"s","author":{"id":"s"},"editor":{"id":"s"}}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["author"],"data":{"bio":"s"}},{"id":"0","subPath":["editor"],"data":{"bio":"s"}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=1
plain twin: A=1, B=1, C=1
```
