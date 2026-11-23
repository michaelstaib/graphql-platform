# Defer_Should_WaitForTheStepOfTheNestedFragment_When_ItsOwnStepLeavesTheObjectThatTheNestedStepAlsoFetches

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"price":123.456}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":["product"],"label":"x"}],"incremental":[{"id":"0","data":{"author":{"note":null}}},{"id":"2","subPath":["author"],"data":{"age":123}}],"completed":[{"id":"0"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
