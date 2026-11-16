# Defer_Should_FailBothGroups_When_TheEntityOfBothIsMissingBelowAnObjectOfAThirdGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"c"},{"id":"3","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"3","data":{"price":123.456}}],"completed":[{"id":"3"}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","bio"],"extensions":{"code":"HC0018"}}]},{"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","bio"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=3, P=1
```
