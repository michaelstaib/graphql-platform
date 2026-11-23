# Defer_Should_MatchThePlainTwin_When_TwoFragmentsWithSwitchesSelectFieldsWithRequirements

## Payload 1

```json
{"data":{"product":{"usd":90}},"pending":[{"id":"0","path":["product"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":80}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=3, B=2
```
