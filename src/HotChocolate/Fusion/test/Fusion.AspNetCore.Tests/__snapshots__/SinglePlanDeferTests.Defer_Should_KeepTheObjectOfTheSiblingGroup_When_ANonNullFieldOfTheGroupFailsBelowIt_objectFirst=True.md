# Defer_Should_KeepTheObjectOfTheSiblingGroup_When_ANonNullFieldOfTheGroupFailsBelowIt

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","bio"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
