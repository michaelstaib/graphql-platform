# Defer_Should_FetchTheDependentField_When_TheOperationThatSuppliesItsKeyDeliversToAnotherGroup

## Payload 1

```json
{"data":{"current":{"user":{"sku":"s"}}},"pending":[{"id":"0","path":["current","user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"total":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=2, D=2
plain twin: A=1, B=1, C=1, D=1
```
