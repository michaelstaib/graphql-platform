# Defer_Should_FailTheGroupAndKeepTheAnchor_When_ANonNullDeferredFieldFails

## Payload 1

```json
{"data":{"featured":{"name":"Product: UHJvZHVjdDoxMzkzOTMyNTQ3"}},"pending":[{"id":"0","path":["featured"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["featured","price"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
