# Defer_Should_NotSendTheKeyLookup_When_TheOperationThatTakesTheKeyIsNotNeeded

## Payload 1

```json
{"data":{"first":{"name":"s","author":{"id":"s"}},"second":{"name":"s","editor":{"nick":"s"}}},"pending":[{"id":"0","path":["first","author"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"bio":"s"}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=2, D=1
plain twin: A=1, B=1, C=1, D=1
```
