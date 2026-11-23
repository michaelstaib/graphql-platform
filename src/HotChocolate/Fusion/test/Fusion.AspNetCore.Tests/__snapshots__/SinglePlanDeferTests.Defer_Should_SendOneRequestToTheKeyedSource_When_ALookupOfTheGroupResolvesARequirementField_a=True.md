# Defer_Should_SendOneRequestToTheKeyedSource_When_ALookupOfTheGroupResolvesARequirementField

## Payload 1

```json
{"data":{"user":{}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["user"]}],"incremental":[{"id":"0","data":{"sku":"s"}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"shipping":0,"total":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, D=1
plain twin: A=1, B=1, D=1
```
