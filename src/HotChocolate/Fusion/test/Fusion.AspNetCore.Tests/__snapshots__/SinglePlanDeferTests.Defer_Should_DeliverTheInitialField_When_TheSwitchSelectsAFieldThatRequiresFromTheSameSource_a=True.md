# Defer_Should_DeliverTheInitialField_When_TheSwitchSelectsAFieldThatRequiresFromTheSameSource

## Payload 1

```json
{"data":{"user":{"shipping":1010}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"tax":2101}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, D=2
plain twin: A=1, B=1, D=1
```
