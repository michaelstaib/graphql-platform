# Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsAndAnInitialFieldRequiresItsRows

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","package"]}],"data":{"product":null}}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
