# Defer_Should_FailTheGroup_When_ItsAnswerIsNullBelowAListOfTheSiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","authors",0,"bio"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"authors":[{"id":"QXV0aG9yOjU4NTQ5NzI2MQ=="},{"id":"QXV0aG9yOjM2OTAzMzUxMjY="},{"id":"QXV0aG9yOjEyMzYyNTE4Mjc="}]}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
