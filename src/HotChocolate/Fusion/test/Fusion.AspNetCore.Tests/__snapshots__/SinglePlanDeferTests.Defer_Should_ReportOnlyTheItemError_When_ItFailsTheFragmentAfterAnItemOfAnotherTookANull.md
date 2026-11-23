# Defer_Should_ReportOnlyTheItemError_When_ItFailsTheFragmentAfterAnItemOfAnotherTookANull

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","parts",0,"weight"],"extensions":{"code":"HC0018"}}],"data":{"package":{"parts":[null,{"weight":123.456},{"weight":123.456}]}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"C failed 0","path":["product","reviews",0]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
