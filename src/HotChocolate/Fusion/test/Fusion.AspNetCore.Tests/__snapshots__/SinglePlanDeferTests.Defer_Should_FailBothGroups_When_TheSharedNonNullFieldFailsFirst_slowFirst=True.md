# Defer_Should_FailBothGroups_When_TheSharedNonNullFieldFailsFirst

## Payload 1

```json
{"data":{},"pending":[{"id":"0","path":[],"label":"R"},{"id":"1","path":[],"label":"P"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":[],"label":"C"}],"incremental":[{"id":"1","data":{"slow":"Query"}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["bad"],"extensions":{"code":"HC0018"}}]}],"hasNext":true}
```

## Payload 4

```json
{"completed":[{"id":"2","errors":[{"message":"Cannot return null for non-nullable field.","path":["bad"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=2, B=1
```
