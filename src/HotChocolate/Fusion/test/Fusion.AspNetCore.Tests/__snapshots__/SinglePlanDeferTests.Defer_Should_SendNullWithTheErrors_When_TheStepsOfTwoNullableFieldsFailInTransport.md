# Defer_Should_SendNullWithTheErrors_When_TheStepsOfTwoNullableFieldsFailInTransport

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","cost"]},{"message":"Unexpected Execution Error","path":["product","stars"]}],"data":{"cost":null,"stars":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, P=1, R=1
```
