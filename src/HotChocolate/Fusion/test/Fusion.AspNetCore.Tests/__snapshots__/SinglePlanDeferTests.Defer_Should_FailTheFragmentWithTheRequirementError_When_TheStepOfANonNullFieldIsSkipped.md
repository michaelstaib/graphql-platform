# Defer_Should_FailTheFragmentWithTheRequirementError_When_TheStepOfANonNullFieldIsSkipped

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","shipping"]}]}],"hasNext":false}
```

## Requests

```text
A=1
```
