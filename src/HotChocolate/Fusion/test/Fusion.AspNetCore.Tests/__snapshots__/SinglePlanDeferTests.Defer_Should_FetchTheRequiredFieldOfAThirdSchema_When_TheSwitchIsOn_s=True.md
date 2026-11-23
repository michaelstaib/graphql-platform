# Defer_Should_FetchTheRequiredFieldOfAThirdSchema_When_TheSwitchIsOn

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"shipping"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"shipping":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
