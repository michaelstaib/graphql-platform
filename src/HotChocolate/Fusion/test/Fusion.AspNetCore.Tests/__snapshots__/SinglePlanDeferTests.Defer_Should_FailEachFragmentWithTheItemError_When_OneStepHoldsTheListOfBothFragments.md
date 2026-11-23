# Defer_Should_FailEachFragmentWithTheItemError_When_OneStepHoldsTheListOfBothFragments

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"C failed 0","path":["product","reviews",0]}]},{"id":"1","errors":[{"message":"C failed 0","path":["product","reviews",0]}]}],"hasNext":false}
```

## Requests

```text
A=1, C=1
```
