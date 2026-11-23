# Defer_Should_SendNullForTheFailedField_When_OnlyTheLookupOfTheNullableRequirementFails

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","weight"]}],"data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, W=2
```
