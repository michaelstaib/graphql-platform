# Defer_Should_NotSendTheSharedFieldAgain_When_TheInnerGroupCompletesAfterTheOuterGroup

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":[],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"bio":"s"}}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=2, C=2, D=1
plain twin: A=1, B=1, C=1, D=1
```
