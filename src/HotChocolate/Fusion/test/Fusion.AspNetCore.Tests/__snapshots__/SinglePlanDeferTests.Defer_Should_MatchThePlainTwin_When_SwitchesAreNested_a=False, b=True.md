# Defer_Should_MatchThePlainTwin_When_SwitchesAreNested

## Delivery

```text
initial {"product":{"name":"s","price":0}}
completed inner
incremental inner subPath [] {"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}
pending inner at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
