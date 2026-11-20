# Defer_Should_DeliverTheInitialField_When_NestedFragmentsSelectFieldsThatRequireFromOneSource

## Payload 1

```json
{"data":{"user":{"shipping":1010,"tax":2101}}}
```

## Requests

```text
deferred: A=1, B=1, D=1, E=1
plain twin: A=1, B=1, D=1, E=1
```
