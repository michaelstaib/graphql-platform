# Defer_Should_DeliverTheNestedFragment_When_TheFailedStepOfItsParentDoesNotFeedItsStep

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"t"},{"id":"1","path":["product"],"label":"g"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"x":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":["product"],"label":"c"}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"2","data":{"price":123.456}}],"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
