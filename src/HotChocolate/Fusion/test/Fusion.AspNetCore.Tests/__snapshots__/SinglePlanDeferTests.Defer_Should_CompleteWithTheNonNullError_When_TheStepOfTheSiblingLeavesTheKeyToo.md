# Defer_Should_CompleteWithTheNonNullError_When_TheStepOfTheSiblingLeavesTheKeyToo

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"p"},{"id":"1","path":["product"],"label":"q"},{"id":"2","path":["product"],"label":"r"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"2","data":{"price":123.456}}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","id"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","bio"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, P=1
```
