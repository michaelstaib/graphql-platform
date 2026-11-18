# Defer_Should_ReportOnlyTheNullError_When_AStepFailsInTransportForAFieldThatAlreadyHoldsAValue

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"],"label":"g"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=3
```
