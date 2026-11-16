# Defer_Should_ReportTheErrorWithoutAPath_When_TheGroupFailsOnANonNullField

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"The source failed."},{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
