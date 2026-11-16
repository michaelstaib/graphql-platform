# Defer_Should_NeverAnnounceTheChild_When_TheParentGroupFailsWhileTheChildRequestIsInFlight

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"P"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","bad"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
