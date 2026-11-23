# Defer_Should_ReportOnlyTheLengthError_When_ASharedListFailsItAfterAnItemOfAnotherTookANull

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","spares",0,"weight"],"extensions":{"code":"HC0018"}}],"data":{"package":{"spares":[null,{"weight":123.456},{"weight":123.456}]}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":["package"],"data":{"parts":[{"size":123.456},{"size":123.456}]}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"2","errors":[{"message":"Cannot merge shared list results with different lengths.","path":["product","package","parts"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1
```
