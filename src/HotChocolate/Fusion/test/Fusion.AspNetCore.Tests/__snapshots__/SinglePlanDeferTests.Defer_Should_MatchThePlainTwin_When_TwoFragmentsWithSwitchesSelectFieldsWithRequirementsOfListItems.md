# Defer_Should_MatchThePlainTwin_When_TwoFragmentsWithSwitchesSelectFieldsWithRequirementsOfListItems

## Payload 1

```json
{"data":{"products":[{"upc":"u1","usd":90},{"upc":"u2","usd":90}]},"pending":[{"id":"0","path":["products",0]},{"id":"1","path":["products",1]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"eur":80}},{"id":"1","data":{"eur":80}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=3, B=2
```
