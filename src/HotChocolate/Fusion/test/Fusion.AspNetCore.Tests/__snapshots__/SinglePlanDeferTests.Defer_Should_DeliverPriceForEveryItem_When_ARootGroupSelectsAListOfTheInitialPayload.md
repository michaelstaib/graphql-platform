# Defer_Should_DeliverPriceForEveryItem_When_ARootGroupSelectsAListOfTheInitialPayload

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["products",0],"data":{"price":0}},{"id":"0","subPath":["products",1],"data":{"price":0}},{"id":"0","subPath":["products",2],"data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
