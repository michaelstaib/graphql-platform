# Defer_Should_CompleteTheGroupWithTheNonNullError_When_AListItRequiresHoldsANull

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","parcels",0,"weight"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=2
```
