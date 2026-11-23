# Defer_Should_SendTheRequirement_When_AStepWithoutAGroupFetchesIt

## Payload 1

```json
{"data":{"product":{"name":"s","shipping":0}},"pending":[{"id":"0","path":["product"],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"weight":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, C=1
plain twin: A=1, C=1
```
