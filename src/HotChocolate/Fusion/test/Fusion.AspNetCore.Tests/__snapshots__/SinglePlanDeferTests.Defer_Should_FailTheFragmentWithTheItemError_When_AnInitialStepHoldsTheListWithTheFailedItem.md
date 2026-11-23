# Defer_Should_FailTheFragmentWithTheItemError_When_AnInitialStepHoldsTheListWithTheFailedItem

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":null}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed s0","path":["product","package","strict",0]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
