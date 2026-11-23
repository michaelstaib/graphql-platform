# Defer_Should_DeliverTheSharedFieldOfBothNestedFragments_When_TheStepThatFeedsOneLookupFails

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"],"label":"g"},{"id":"2","path":["product"],"label":"h"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"3","path":["product","author"],"label":"k"}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 4

```json
{"pending":[{"id":"4","path":["product","author"],"label":"c"}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 5

```json
{"incremental":[{"id":"3","data":{"bio":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}],"completed":[{"id":"3"},{"id":"4"}],"hasNext":false}
```

## Requests

```text
A=1, B=3, C=1
```
