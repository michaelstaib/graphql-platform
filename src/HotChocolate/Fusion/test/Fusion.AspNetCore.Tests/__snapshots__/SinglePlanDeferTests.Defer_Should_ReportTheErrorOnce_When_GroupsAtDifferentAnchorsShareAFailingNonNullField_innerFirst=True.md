# Defer_Should_ReportTheErrorOnce_When_GroupsAtDifferentAnchorsShareAFailingNonNullField

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":[],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","price"]}]}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["product","price"]}]}],"hasNext":false}
```

## Requests

```text
A=2, B=2
```
