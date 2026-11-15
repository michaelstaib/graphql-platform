# Defer_Should_MatchThePlainTwin_When_PriceLivesOnTheParentSchema

## Delivery

```text
initial {"product":{"name":"s"}}
completed price
completed reviews
incremental price subPath [] {"price":0}
incremental reviews subPath [] {"reviews":[{"body":"s"},{"body":"s"},{"body":"s"}]}
pending price at ["product"]
pending reviews at ["product"]
```

## Requests

```text
deferred: A=2, C=1
plain twin: A=1, C=1
```
