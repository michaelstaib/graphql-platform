# Defer_Should_FetchTheListOnce_When_ATypedFragmentSelectsItBelowAListOfTheInitialPayload

## Payload 1

```json
{"data":{"search":[{"name":"s","reviews":[{"id":"s","rating":0},{"id":"s","rating":0},{"id":"s","rating":0}]},{},{"name":"s","reviews":[{"id":"s","rating":0},{"id":"s","rating":0},{"id":"s","rating":0}]}]}}
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
