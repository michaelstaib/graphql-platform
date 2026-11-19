# Defer_Should_CompleteTheGroupWithTheNonNullError_When_TheKeyIsNullAndAFieldIsRenamed

## Payload 1

```json
{"data":{"item":{"__typename":"Book","size":123,"title":"Book: Qm9vazoxOTg5OTQxMzA1"}},"pending":[{"id":"0","path":["item"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["item","id"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1
```
