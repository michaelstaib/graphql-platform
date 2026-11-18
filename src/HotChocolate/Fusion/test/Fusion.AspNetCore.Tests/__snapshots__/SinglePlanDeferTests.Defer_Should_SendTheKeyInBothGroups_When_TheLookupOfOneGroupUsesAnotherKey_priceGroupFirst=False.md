# Defer_Should_SendTheKeyInBothGroups_When_TheLookupOfOneGroupUsesAnotherKey

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"d"},{"id":"1","path":["products",1],"label":"d"},{"id":"2","path":["products",2],"label":"d"},{"id":"3","path":["products",0],"label":"e"},{"id":"4","path":["products",1],"label":"e"},{"id":"5","path":["products",2],"label":"e"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"3","data":{"id":"s"}},{"id":"4","data":{"id":"s"}},{"id":"5","data":{"id":"s"}}],"completed":[{"id":"3"},{"id":"4"},{"id":"5"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"price":0}},{"id":"1","data":{"price":0}},{"id":"2","data":{"price":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
