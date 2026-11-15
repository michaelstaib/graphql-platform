# Defer_Should_MatchThePlainTwin_When_TwoSwitchesSelectFieldsOfOtherSchemas

## Delivery

```text
initial {"product":{"name":"s","reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}}
completed price
incremental price subPath [] {"price":0}
pending price at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
