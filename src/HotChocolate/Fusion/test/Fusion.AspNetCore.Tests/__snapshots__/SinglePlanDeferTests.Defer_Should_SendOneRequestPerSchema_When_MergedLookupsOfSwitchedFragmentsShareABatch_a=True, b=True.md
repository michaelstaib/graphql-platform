# Defer_Should_SendOneRequestPerSchema_When_MergedLookupsOfSwitchedFragmentsShareABatch

## Payload 1

```json
{"data":{"product":{"name":"s"},"topProduct":{"name":"s"}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["topProduct"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product","author"]},{"id":"3","path":["product","author"]}],"incremental":[{"id":"0","data":{"author":{}}},{"id":"1","data":{"author":{"id":"s"}}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","data":{"bio":"s"}},{"id":"3","data":{"nick":"s"}}],"completed":[{"id":"2"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
