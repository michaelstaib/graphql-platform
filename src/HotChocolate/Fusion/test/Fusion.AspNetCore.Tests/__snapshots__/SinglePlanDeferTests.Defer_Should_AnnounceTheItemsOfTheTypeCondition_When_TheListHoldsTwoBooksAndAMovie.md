# Defer_Should_AnnounceTheItemsOfTheTypeCondition_When_TheListHoldsTwoBooksAndAMovie

## Payload 1

```json
{"data":{"items":[{"id":"s"},{"id":"s"},{"id":"s"}]},"pending":[{"id":"0","path":["items",0],"label":"b"},{"id":"1","path":["items",1],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}},{"id":"1","data":{"rating":0}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
