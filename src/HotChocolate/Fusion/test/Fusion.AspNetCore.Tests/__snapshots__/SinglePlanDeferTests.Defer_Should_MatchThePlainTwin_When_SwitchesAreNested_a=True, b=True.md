# Defer_Should_MatchThePlainTwin_When_SwitchesAreNested

## Delivery

```text
initial {"product":{"name":"s"}}
completed inner
completed outer
incremental inner subPath [] {"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}
incremental outer subPath [] {"price":0}
pending inner at ["product"]
pending outer at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
