# Defer_Should_ReportTheItemErrorOnce_When_TwoFragmentsHoldTheListWithTheNullItem

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":["product"],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","reviews",0],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","reviews",0],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, C=2
```
