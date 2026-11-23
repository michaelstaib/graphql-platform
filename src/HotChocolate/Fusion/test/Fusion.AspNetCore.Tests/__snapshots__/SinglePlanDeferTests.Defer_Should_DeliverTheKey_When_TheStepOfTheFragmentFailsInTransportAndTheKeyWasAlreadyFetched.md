# Defer_Should_DeliverTheKey_When_TheStepOfTheFragmentFailsInTransportAndTheKeyWasAlreadyFetched

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"id":"UHJvZHVjdDo3NzM3NjQwMDE="}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2
```
