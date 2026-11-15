# Defer_Should_MatchThePlainTwin_When_SwitchesAreNested

## Delivery

```text
initial {"product":{"name":"s"}}
completed outer
incremental outer subPath [] {"price":0,"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}
pending outer at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
