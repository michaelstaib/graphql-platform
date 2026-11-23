# Defer_Should_MatchThePlainTwin_When_ThreeNestedSwitchesSelectAFieldThatRequiresTheWeight

## Payload 1

```json
{"data":{"user":{}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"shipping":1010}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, D=1
plain twin: A=1, B=1, D=1
```
