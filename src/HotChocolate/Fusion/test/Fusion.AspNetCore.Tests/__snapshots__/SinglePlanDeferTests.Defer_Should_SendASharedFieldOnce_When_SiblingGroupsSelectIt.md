# Defer_Should_SendASharedFieldOnce_When_SiblingGroupsSelectIt

## Payload 1

```json
{"data":{"product":{}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0,"rating":0}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
