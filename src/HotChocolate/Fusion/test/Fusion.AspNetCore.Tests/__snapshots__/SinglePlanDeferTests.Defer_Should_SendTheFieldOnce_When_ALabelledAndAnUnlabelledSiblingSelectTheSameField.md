# Defer_Should_SendTheFieldOnce_When_ALabelledAndAnUnlabelledSiblingSelectTheSameField

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"]},{"id":"1","path":["product"],"label":"labelled"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"price":0}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
