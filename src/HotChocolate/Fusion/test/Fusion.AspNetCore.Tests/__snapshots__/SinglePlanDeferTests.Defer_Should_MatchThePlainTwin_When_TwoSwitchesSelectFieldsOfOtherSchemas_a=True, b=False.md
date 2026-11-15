# Defer_Should_MatchThePlainTwin_When_TwoSwitchesSelectFieldsOfOtherSchemas

## Payload 1

```json
{"data":{"product":{"name":"s","reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}},"pending":[{"id":"0","path":["product"],"label":"price"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
