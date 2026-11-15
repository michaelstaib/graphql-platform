# Defer_Should_MatchThePlainTwin_When_PriceLivesOnTheParentSchema

## Payload 1

```json
{"data":{"product":{"name":"s","price":0}},"pending":[{"id":"1","path":["product"],"label":"reviews"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, C=1
plain twin: A=1, C=1
```
