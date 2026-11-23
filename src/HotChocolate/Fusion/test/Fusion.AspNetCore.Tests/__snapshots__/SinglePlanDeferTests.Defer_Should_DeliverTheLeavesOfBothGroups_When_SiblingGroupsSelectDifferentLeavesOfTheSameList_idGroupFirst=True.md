# Defer_Should_DeliverTheLeavesOfBothGroups_When_SiblingGroupsSelectDifferentLeavesOfTheSameList

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":["reviews",0],"data":{"body":"s"}},{"id":"1","subPath":["reviews",1],"data":{"body":"s"}},{"id":"1","subPath":["reviews",2],"data":{"body":"s"}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
