# Defer_Should_DeliverBothNestedFragments_When_UnlabelledSiblingsShareTheOuterStep

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product","author"]},{"id":"3","path":["product","author"]}],"incremental":[{"id":"0","data":{"author":{}}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"3","data":{"bio":"s"}}],"completed":[{"id":"3"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=2
plain twin: A=1, B=1, C=1
```
