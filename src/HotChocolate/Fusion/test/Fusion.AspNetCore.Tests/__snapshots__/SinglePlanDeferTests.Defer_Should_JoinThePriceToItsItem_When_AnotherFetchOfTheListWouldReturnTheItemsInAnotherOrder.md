# Defer_Should_JoinThePriceToItsItem_When_AnotherFetchOfTheListWouldReturnTheItemsInAnotherOrder

## Payload 1

```json
{"data":{"products":[{"name":"n-p0"},{"name":"n-p1"},{"name":"n-p2"}]},"pending":[{"id":"0","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["products",0],"data":{"price":10}},{"id":"0","subPath":["products",1],"data":{"price":11}},{"id":"0","subPath":["products",2],"data":{"price":12}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
