# Defer_Should_StartTheNestedLookupBeforeTheOuterLookupAnswers_When_TheKeyIsKnown

## Delivery

```text
initial {"product":{"name":"s"}}
completed i
completed o
incremental i subPath [] {"rating":0}
incremental o subPath [] {"price":0}
pending i at ["product"]
pending o at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
