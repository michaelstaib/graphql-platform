# Defer_Should_DeliverTheFieldsOfEveryRow_When_ASkipDecidesWhetherTheOnlyStepRuns

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":["products",0],"label":"d"},{"id":"1","path":["products",1],"label":"d"},{"id":"2","path":["products",2],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"id":"s"}},{"id":"1","data":{"id":"s"}},{"id":"2","data":{"id":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1
plain twin: A=1
```
