# Defer_Should_AnnounceOneIdPerItem_When_TheAnchorIsAListOfThreeItems

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"p"},{"id":"1","path":["products",1],"label":"p"},{"id":"2","path":["products",2],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":123.456}},{"id":"1","data":{"price":123.456}},{"id":"2","data":{"price":123.456}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
