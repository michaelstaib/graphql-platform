# Defer_Should_SendTheRequirement_When_ASiblingGroupDependsOnIt

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"shipping":0}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"weight":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, C=1
plain twin: A=1, C=1
```
