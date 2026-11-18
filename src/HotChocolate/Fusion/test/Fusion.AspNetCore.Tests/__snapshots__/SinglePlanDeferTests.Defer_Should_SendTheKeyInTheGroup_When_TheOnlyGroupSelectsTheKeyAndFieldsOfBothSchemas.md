# Defer_Should_SendTheKeyInTheGroup_When_TheOnlyGroupSelectsTheKeyAndFieldsOfBothSchemas

## Payload 1

```json
{"data":{"product":{}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"id":"s","name":"s","price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=1
plain twin: A=1, B=1
```
