# Defer_Should_ReportTheRootError_When_TheSourceOfTheFailedGroupAnswersWithoutData

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"reviews":[{"body":"Review: UmV2aWV3OjEzNTc0MDc5MTc="},{"body":"Review: UmV2aWV3OjMxMTc0ODAzMzU="},{"body":"Review: UmV2aWV3Ojg1Njc4NjI5MQ=="}]}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"The source failed."},{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
