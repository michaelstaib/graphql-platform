# Defer_Should_ReportTheTransportError_When_AnotherStepOfTheGroupReportsAnErrorOfANullableField

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"x":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Unexpected Execution Error"},{"message":"Unexpected Execution Error","path":["product","price"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
