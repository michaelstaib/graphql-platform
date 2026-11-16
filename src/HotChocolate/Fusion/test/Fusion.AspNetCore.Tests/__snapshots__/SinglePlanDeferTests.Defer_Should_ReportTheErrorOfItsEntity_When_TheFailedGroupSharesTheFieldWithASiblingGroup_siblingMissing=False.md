# Defer_Should_ReportTheErrorOfItsEntity_When_TheFailedGroupSharesTheFieldWithASiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"The entity failed."},{"message":"Cannot return null for non-nullable field.","path":["product","price"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"2","subPath":["author"],"data":{"note":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}],"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=3, P=1
```
