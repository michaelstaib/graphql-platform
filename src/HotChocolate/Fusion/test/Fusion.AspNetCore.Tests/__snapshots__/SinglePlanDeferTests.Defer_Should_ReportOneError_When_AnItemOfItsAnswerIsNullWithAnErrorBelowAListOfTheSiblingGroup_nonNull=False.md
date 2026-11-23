# Defer_Should_ReportOneError_When_AnItemOfItsAnswerIsNullWithAnErrorBelowAListOfTheSiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"authors":[{"id":"QXV0aG9yOjU4NTQ5NzI2MQ=="},{"id":"QXV0aG9yOjM2OTAzMzUxMjY="},{"id":"QXV0aG9yOjEyMzYyNTE4Mjc="}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","errors":[{"message":"The item failed.","path":["product","authors",0,"note"]}],"subPath":["authors",0],"data":{"note":null}},{"id":"1","subPath":["authors",1],"data":{"note":"Author: QXV0aG9yOjM2OTAzMzUxMjY="}},{"id":"1","subPath":["authors",2],"data":{"note":"Author: QXV0aG9yOjEyMzYyNTE4Mjc="}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
