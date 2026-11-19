# Defer_Should_FailOnlyTheFragmentOfTheItem_When_TheLookupOfOneListItemIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","authors":[{},{},{}]}},"pending":[{"id":"0","path":["product","authors",0],"label":"a"},{"id":"1","path":["product","authors",1],"label":"a"},{"id":"2","path":["product","authors",2],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"bio":"Author: QXV0aG9yOjM2OTAzMzUxMjY="}},{"id":"2","data":{"bio":"Author: QXV0aG9yOjEyMzYyNTE4Mjc="}}],"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","authors",0,"bio"],"extensions":{"code":"HC0018"}}]},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
