# Defer_Should_DeliverTheNestedFragmentSeparately_When_AnotherNodeSelectsTheParentFieldWithoutIt

## Payload 1

```json
{"data":{"shelf":{"id":"s"}},"pending":[{"id":"0","path":[],"label":"outer"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["shelf","top"],"label":"inner"}],"incremental":[{"id":"0","subPath":["shelf"],"data":{"top":{}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"price":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
