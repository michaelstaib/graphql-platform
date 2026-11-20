# Defer_Should_DeliverTheFieldsOfBothDogs_When_TheFragmentHoldsTwoTypeConditionsAndOneNeedsALookup

## Payload 1

```json
{"data":{"current":{"pets":[{},{},{}]}},"pending":[{"id":"0","path":["current","pets",0]},{"id":"1","path":["current","pets",1]},{"id":"2","path":["current","pets",2]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"bark":"s","owner":{"name":"s"}}},{"id":"1","data":{"meow":"s"}},{"id":"2","data":{"bark":"s","owner":{"name":"s"}}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, E=1, P=1
plain twin: A=1, E=1, P=1
```
