# Defer_Should_SendNullForTheFailedField_When_OnlyTheLookupOfTheRequirementFailsAndNullsStay

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","package"]}],"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"package":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
