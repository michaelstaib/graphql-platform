# Defer_Should_SendTheErrorWithTheNullObject_When_TheNullOfAFailedLookupStopsInAnEnclosingFragment

## Payload 1

```json
{"data":{"product":{"name":"s","shipNullable":null}},"pending":[{"id":"0","path":["product"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","maybePack","h2"]}],"data":{"maybePack":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, D=1
plain twin: A=1, B=1, D=1
```
