# Defer_Should_SendNullForTheNullableParent_When_TheStepOfItsNonNullFieldFailsInTransport

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"g"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","author","bio"]}],"data":{"author":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
