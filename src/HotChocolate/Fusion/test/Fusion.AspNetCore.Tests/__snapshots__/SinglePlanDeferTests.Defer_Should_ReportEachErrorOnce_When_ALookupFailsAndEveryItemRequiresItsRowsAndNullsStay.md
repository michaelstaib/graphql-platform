# Defer_Should_ReportEachErrorOnce_When_ALookupFailsAndEveryItemRequiresItsRowsAndNullsStay

## Payload 1

```json
{"errors":[{"message":"Unexpected Execution Error","path":["products",0,"package"]},{"message":"Unexpected Execution Error","path":["products",1,"package"]},{"message":"Unexpected Execution Error","path":["products",2,"package"]}],"data":{"products":[{"name":"s","shipping":null},{"name":"s","shipping":null},{"name":"s","shipping":null}]},"pending":[{"id":"0","path":["products",0],"label":"d"},{"id":"1","path":["products",1],"label":"d"},{"id":"2","path":["products",2],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"package":null}},{"id":"1","data":{"package":null}},{"id":"2","data":{"package":null}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
