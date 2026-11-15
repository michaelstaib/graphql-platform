# Defer_Should_SendTheKeyOfEveryItem_When_TheGroupSelectsTheKeyOfItsLookup

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":[0],"data":{"id":"s","price":0}},{"id":"0","subPath":[1],"data":{"id":"s","price":0}},{"id":"0","subPath":[2],"data":{"id":"s","price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
