# Defer_Should_MatchThePlainTwin_When_SwitchesAreNested

## Payload 1

```json
{"data":{"product":{"name":"s","price":0}},"pending":[{"id":"0","path":["product"],"label":"inner"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
