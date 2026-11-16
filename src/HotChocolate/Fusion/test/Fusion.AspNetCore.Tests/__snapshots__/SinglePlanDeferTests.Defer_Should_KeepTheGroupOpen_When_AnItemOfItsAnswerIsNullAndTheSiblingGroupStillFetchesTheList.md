# Defer_Should_KeepTheGroupOpen_When_AnItemOfItsAnswerIsNullAndTheSiblingGroupStillFetchesTheList

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"c"},{"id":"3","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"authors":[{"id":"QXV0aG9yOjU4NTQ5NzI2MQ=="},{"id":"QXV0aG9yOjM2OTAzMzUxMjY="},{"id":"QXV0aG9yOjEyMzYyNTE4Mjc="}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"3","data":{"price":123.456}}],"completed":[{"id":"3"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"1","subPath":["authors",0],"data":{"bio":"Author: QXV0aG9yOjU4NTQ5NzI2MQ=="}},{"id":"1","subPath":["authors",1],"data":{"bio":"Author: QXV0aG9yOjM2OTAzMzUxMjY="}},{"id":"1","subPath":["authors",2],"data":{"bio":"Author: QXV0aG9yOjEyMzYyNTE4Mjc="}}],"completed":[{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=3, P=1
```
