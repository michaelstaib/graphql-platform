# Defer_Should_MatchThePlainTwin_When_SiblingFragmentsSelectRequirementsOfOneField

## Payload 1

```json
{"data":{"product":{}},"pending":[{"id":"0","path":["product"],"label":"eur"},{"id":"1","path":["product"],"label":"usd"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":80}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"usd":90}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=3, B=2
```
