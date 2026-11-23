# Defer_Should_CompleteTheOuterGroupWithTheNonNullError_When_ANestedGroupReadsTheNullKeyToo

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","id"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1
```
