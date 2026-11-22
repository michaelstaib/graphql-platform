# Defer_Should_SendTheInnerErrorWithTheDeliveringFragment_When_AFailedFragmentHoldsTheSameRow

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","strict",0],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","parts",0,"weight"],"extensions":{"code":"HC0018"}}],"data":{"package":{"parts":[null,{"weight":123.456},{"weight":123.456}]}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
