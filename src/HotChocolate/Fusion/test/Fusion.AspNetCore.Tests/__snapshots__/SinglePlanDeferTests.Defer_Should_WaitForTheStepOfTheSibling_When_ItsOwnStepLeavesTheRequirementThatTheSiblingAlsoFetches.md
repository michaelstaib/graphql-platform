# Defer_Should_WaitForTheStepOfTheSibling_When_ItsOwnStepLeavesTheRequirementThatTheSiblingAlsoFetches

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"r"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"2","data":{"price":123.456}}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"weight":123.456}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"1","data":{"shipping":123.456}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1, P=1
```
