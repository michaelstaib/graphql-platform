# Defer_Should_DeliverOnlyTheFieldsLeft_When_AFieldIsSelectedInAnInitialAndADeferredSwitch

## Delivery

```text
initial {"product":{"price":0}}
completed a
incremental a subPath [] {"rating":0}
pending a at ["product"]
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
