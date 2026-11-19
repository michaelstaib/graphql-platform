# Defer_Should_CompleteTheRowWithoutTheStepError_When_AnotherStepFailsAndItsLookupIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"g"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","price"]}],"data":{"price":null,"author":{"bio":null}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, P=1
```
