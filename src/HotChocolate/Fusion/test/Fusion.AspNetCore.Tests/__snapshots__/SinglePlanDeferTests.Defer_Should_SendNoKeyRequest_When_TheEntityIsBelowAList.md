# Defer_Should_SendNoKeyRequest_When_TheEntityIsBelowAList

## Payload 1

```json
{"data":{"products":[{"upc":"s","related":{"upc":"s"}},{"upc":"s","related":{"upc":"s"}},{"upc":"s","related":{"upc":"s"}}]},"pending":[{"id":"0","path":["products",0,"related"]},{"id":"1","path":["products",1,"related"]},{"id":"2","path":["products",2,"related"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":0}},{"id":"1","data":{"eur":0}},{"id":"2","data":{"eur":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=2, B=1
```
