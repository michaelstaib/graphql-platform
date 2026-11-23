# Defer_Should_FailTheFragmentWithTheSourceError_When_TheKeyOfTheLookupIsNullWithAnError

## Payload 1

```json
{"errors":[{"message":"A failed","path":["products",0,"sku"]}],"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk=","sku":null},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5","sku":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2","sku":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0]},{"id":"1","path":["products",1]},{"id":"2","path":["products",2]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"priceNn":123.456}},{"id":"2","data":{"priceNn":123.456}}],"completed":[{"id":"0","errors":[{"message":"A failed","path":["products",0,"priceNn"]}]},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
