# Defer_Should_KeepTheObjectsOfTheInitialPayload_When_ARequiredFieldBelowTheAnchorFails

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","reviews":[{"id":"UmV2aWV3OjI5MjMwNjU0NzI="},{"id":"UmV2aWV3OjI0MjM1MzY4NDI="},{"id":"UmV2aWV3OjM0NzAyNzQ2ODM="}]}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","reviews",0,"rating"]},{"message":"Unexpected Execution Error","path":["product","reviews",1,"rating"]},{"message":"Unexpected Execution Error","path":["product","reviews",2,"rating"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1
```
