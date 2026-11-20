# Defer_Should_ReturnASingleResultWithTheError_When_ALookupFailsBelowNonNullObjectsUpToTheAnchor

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","pack","h2"]}],"data":{"product":null}}
```

## Requests

```text
deferred: A=1, B=1, D=1
plain twin: A=1, B=1, D=1
```
