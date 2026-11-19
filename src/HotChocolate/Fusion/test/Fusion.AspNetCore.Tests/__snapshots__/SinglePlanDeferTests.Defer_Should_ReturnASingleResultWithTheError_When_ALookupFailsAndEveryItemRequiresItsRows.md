# Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsAndEveryItemRequiresItsRows

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["products",0,"package"]}],"data":null}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
