# Defer_Should_AnnounceTheGroupOfAPartOnceAtItsPlace_When_ACopyOfANestedSwitchIsRemoved

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}],"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"outer"},{"id":"1","path":[],"label":"top"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","subPath":["products",0],"data":{"price":0}},{"id":"1","subPath":["products",1],"data":{"price":0}},{"id":"1","subPath":["products",2],"data":{"price":0}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"2","path":["product"],"label":"inner"}],"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"2","data":{"rating":0}}],"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=3
plain twin: A=1, B=1
```
