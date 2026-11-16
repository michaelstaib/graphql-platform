# Defer_Should_FailTheGroup_When_ItsEntityFailsTwoObjectsBelowTheAnchor

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"publisher":{"id":"UHVibGlzaGVyOjQ1NDkwMjU1NQ=="}}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","publisher","title"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
