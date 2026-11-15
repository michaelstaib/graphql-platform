# Defer_Should_SendNameOnce_When_AFieldIsPlainUnderAConditionAndDeferred

## Delivery

```text
initial {"product":{"name":"s"}}
completed L
incremental L subPath [] {"price":0}
pending L at ["product"]
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
