# Defer_Should_MatchThePlainTwin_When_TheAnchorIsInsideAList

## Delivery

```text
initial {"products":[{"name":"s"},{"name":"s"},{"name":"s"}]}
completed price
incremental price subPath [0] {"price":0}
incremental price subPath [1] {"price":0}
incremental price subPath [2] {"price":0}
pending price at ["products"]
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
