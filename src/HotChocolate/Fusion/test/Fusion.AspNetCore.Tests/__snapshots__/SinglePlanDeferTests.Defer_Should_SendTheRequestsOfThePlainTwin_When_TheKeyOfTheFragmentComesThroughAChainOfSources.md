# Defer_Should_SendTheRequestsOfThePlainTwin_When_TheKeyOfTheFragmentComesThroughAChainOfSources

## Payload 1

```json
{"data":{"user":{}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"tax":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=1, D=1, E=1
plain twin: A=1, B=1, C=1, D=1, E=1
```
