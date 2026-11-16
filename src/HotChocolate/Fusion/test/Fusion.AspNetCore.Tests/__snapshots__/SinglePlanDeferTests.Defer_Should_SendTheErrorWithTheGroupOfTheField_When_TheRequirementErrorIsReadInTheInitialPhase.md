# Defer_Should_SendTheErrorWithTheGroupOfTheField_When_TheRequirementErrorIsReadInTheInitialPhase

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"shipping":null}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","weight"]}],"data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2
```
