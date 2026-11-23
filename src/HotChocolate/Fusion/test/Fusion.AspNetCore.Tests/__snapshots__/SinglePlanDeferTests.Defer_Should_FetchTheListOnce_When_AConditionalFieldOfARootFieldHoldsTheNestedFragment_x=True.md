# Defer_Should_FetchTheListOnce_When_AConditionalFieldOfARootFieldHoldsTheNestedFragment

## Payload 1

```json
{"data":{"product":{"related":{"price":0,"reviews":[{"stars":0,"id":"s"},{"stars":0,"id":"s"},{"stars":0,"id":"s"}],"name":"s"}}}}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
