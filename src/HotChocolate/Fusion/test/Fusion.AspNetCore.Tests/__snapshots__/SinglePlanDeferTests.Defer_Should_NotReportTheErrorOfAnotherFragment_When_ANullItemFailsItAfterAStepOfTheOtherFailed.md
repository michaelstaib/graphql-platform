# Defer_Should_NotReportTheErrorOfAnotherFragment_When_ANullItemFailsItAfterAStepOfTheOtherFailed

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","nullable"]}],"data":{"nullable":null}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
