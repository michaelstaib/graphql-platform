# Defer_Should_DeliverTheSharedFieldWithTheNestedGroup_When_TheSiblingGroupFails

## Payload 1

```json
{"data":{},"pending":[{"id":"0","path":[],"label":"R"},{"id":"1","path":[],"label":"P"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["bad"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":[],"label":"C"}],"incremental":[{"id":"1","data":{"slow":"Query"}},{"id":"2","data":{"x":"Query"}}],"completed":[{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=2, B=1
```
