# Defer_Should_SendOuterDataBeforeInnerData_When_TheLookupsOfAnOuterAndAnInnerGroupShareARequest

## Payload 1

```json
{"data":{"products":[{"upc":"s"},{"upc":"s"},{"upc":"s"}]},"pending":[{"id":"0","path":["products",0]},{"id":"1","path":["products",1]},{"id":"2","path":["products",2]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"3","path":["products",0]},{"id":"4","path":["products",1]},{"id":"5","path":["products",2]}],"incremental":[{"id":"0","data":{"eur":0}},{"id":"1","data":{"eur":0}},{"id":"2","data":{"eur":0}},{"id":"3","data":{"usd":0}},{"id":"4","data":{"usd":0}},{"id":"5","data":{"usd":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=2, B=1
```
