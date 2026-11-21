# Defer_Should_ReportBothErrors_When_ANullableAndARequiredFieldOfTheGroupFail

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","required"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
