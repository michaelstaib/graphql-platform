# Defer_Should_DeliverBothNestedFragments_When_AnIncludedFieldSelectsTheAuthor

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["product","author"]},{"id":"2","path":["product","author"]}],"incremental":[{"id":"0","data":{"author":{}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"bio":"s"}},{"id":"2","data":{"nick":"s"}}],"completed":[{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
