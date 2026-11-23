# Defer_Should_FailTheFragmentWithTheStepError_When_TheRequiredWeightOfTheInitialPayloadIsNull

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["product","weight"]}],"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","weight":null}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","shipping"]}]}],"hasNext":false}
```

## Requests

```text
A=1
```
