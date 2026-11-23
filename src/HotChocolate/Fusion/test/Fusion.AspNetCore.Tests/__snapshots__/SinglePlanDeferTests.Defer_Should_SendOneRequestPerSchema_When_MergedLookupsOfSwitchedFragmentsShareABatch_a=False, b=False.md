# Defer_Should_SendOneRequestPerSchema_When_MergedLookupsOfSwitchedFragmentsShareABatch

## Payload 1

```json
{"data":{"product":{"name":"s"},"topProduct":{"name":"s"}},"pending":[{"id":"0","path":["topProduct"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"s"}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
