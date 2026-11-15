# Defer_Should_SendTheValueOfItsOwnStep_When_TheChildStepFetchesTheSameFieldFirst

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["product"],"label":"i"}],"incremental":[{"id":"0","data":{"price":123.456}},{"id":"1","data":{"rating":123}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
