# Defer_Should_MatchThePlainTwin_When_SwitchesAreNested

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"1","path":["product"],"label":"outer"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product"],"label":"inner"}],"incremental":[{"id":"1","data":{"price":0}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","data":{"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}}],"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
