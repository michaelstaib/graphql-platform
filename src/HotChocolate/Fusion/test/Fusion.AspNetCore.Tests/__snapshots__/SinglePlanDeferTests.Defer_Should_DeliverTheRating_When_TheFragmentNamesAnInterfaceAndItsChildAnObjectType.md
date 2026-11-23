# Defer_Should_DeliverTheRating_When_TheFragmentNamesAnInterfaceAndItsChildAnObjectType

## Payload 1

```json
{"data":{"item":{"name":"s"}},"pending":[{"id":"0","path":["item"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
