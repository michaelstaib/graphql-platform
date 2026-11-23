# Defer_Should_PropagateTheNullToTheNextNullablePosition_When_TheLookupBelowItIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","authors",0,"bio"],"extensions":{"code":"HC0018"}}],"data":{"authors":[null,{"bio":"Author: QXV0aG9yOjM2OTAzMzUxMjY="},{"bio":"Author: QXV0aG9yOjEyMzYyNTE4Mjc="}]}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
