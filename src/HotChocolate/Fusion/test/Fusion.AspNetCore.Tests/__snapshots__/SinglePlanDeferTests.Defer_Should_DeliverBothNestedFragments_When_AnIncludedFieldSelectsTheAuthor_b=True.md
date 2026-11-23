# Defer_Should_DeliverBothNestedFragments_When_AnIncludedFieldSelectsTheAuthor

## Payload 1

```json
{"data":{"product":{"name":"s","author":{"id":"s"}}},"pending":[{"id":"0","path":["product","author"]},{"id":"1","path":["product","author"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"bio":"s"}},{"id":"1","data":{"nick":"s"}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
