# Defer_Should_SendOneRequestToTheCodeSource_When_TheCodeOfTheUserIsSkipped

## Payload 1

```json
{"data":{"current":{"user":{}}},"pending":[{"id":"0","path":["current","user"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["current","user"]}],"incremental":[{"id":"0","data":{"friend":{"code":"s"}}},{"id":"1","data":{"sku":"s","total":0}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1, C=1, D=1
plain twin: A=1, B=1, C=1, D=1
```
