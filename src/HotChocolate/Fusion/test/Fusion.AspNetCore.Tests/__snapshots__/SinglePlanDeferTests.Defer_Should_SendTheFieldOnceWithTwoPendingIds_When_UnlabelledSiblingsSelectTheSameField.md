# Defer_Should_SendTheFieldOnceWithTwoPendingIds_When_UnlabelledSiblingsSelectTheSameField

## Delivery

```text
initial {"product":{"name":"s"}}
completed <unlabelled 0>
completed <unlabelled 1>
incremental <unlabelled 0> subPath [] {"price":0}
pending <unlabelled 0> at ["product"]
pending <unlabelled 1> at ["product"]
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
