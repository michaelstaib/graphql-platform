# Defer_Should_NotSendTheKeyLookup_When_TheOperationThatTakesTheKeyIsNotNeeded

## Payload 1

```json
{"data":{"first":{"name":"s"},"second":{"name":"s"}},"pending":[{"id":"0","path":["first"]},{"id":"1","path":["second"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["first","author"]}],"incremental":[{"id":"0","data":{"author":{}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"3","path":["second","editor"]}],"incremental":[{"id":"1","data":{"editor":{}}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"2","data":{"bio":"s"}},{"id":"3","data":{"nick":"s"}}],"completed":[{"id":"2"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1, D=1
plain twin: A=1, B=1, C=1, D=1
```
