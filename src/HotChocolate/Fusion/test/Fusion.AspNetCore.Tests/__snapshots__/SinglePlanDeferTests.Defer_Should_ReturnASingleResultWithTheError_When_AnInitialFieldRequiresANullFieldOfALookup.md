# Defer_Should_ReturnASingleResultWithTheError_When_AnInitialFieldRequiresANullFieldOfALookup

## Payload 1

```json
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["product","weight"],"extensions":{"code":"HC0018"}}],"data":{"product":null}}
```

## Requests

```text
A=1, B=1
```
