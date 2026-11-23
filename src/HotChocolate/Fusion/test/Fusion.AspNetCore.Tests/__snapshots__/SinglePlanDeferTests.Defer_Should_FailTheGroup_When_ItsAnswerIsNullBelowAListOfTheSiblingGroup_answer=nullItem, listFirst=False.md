# Defer_Should_FailTheGroup_When_ItsAnswerIsNullBelowAListOfTheSiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"authors":[null,{"bio":"Author: QXV0aG9yOjM2OTAzMzUxMjY="},{"bio":"Author: QXV0aG9yOjEyMzYyNTE4Mjc="}]}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["authors",1],"data":{"id":"QXV0aG9yOjM2OTAzMzUxMjY="}},{"id":"0","subPath":["authors",2],"data":{"id":"QXV0aG9yOjEyMzYyNTE4Mjc="}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
