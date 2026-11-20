# Defer_Should_FetchTheDependentField_When_TheOperationThatSuppliesItsKeyDeliversToAnotherGroup

## Payload 1

```json
{"data":{"current":{"user":{}}},"pending":[{"id":"0","path":["current","user"]},{"id":"1","path":["current","user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"sku":"s"}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"total":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1, D=1
plain twin: A=1, B=1, C=1, D=1
```
