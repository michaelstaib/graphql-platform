# Defer_Should_SendTheNull_When_AStepWithoutGroupsWritesANullableRequirementAsNull

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"shipping":123.456}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2, C=1
```
