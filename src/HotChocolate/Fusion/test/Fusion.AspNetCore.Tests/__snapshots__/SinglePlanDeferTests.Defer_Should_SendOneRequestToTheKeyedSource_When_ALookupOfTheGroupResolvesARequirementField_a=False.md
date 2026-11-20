# Defer_Should_SendOneRequestToTheKeyedSource_When_ALookupOfTheGroupResolvesARequirementField

## Payload 1

```json
{"data":{"user":{"sku":"s"}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"shipping":0,"total":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, D=2
plain twin: A=1, B=1, D=1
```
