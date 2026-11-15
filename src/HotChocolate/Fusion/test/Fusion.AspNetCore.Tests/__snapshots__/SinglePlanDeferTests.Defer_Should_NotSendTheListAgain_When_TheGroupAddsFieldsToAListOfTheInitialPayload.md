# Defer_Should_NotSendTheListAgain_When_TheGroupAddsFieldsToAListOfTheInitialPayload

## Payload 1

```json
{"data":{"product":{"name":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}},"pending":[{"id":"0","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["reviews",0],"data":{"rating":0}},{"id":"0","subPath":["reviews",1],"data":{"rating":0}},{"id":"0","subPath":["reviews",2],"data":{"rating":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=1
plain twin: A=1, B=1, C=1
```
