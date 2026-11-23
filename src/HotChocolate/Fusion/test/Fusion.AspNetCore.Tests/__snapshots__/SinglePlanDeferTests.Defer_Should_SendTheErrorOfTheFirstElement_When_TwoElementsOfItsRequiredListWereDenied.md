# Defer_Should_SendTheErrorOfTheFirstElement_When_TwoElementsOfItsRequiredListWereDenied

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inners":[{"c":"Inner: SW5uZXI6OTEwODM1NDEx"},{"c":"Inner: SW5uZXI6MTk4MDk0NTk4Mg=="},{"c":"Inner: SW5uZXI6MzAzMzYyMTU1NA=="}]}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed 0","path":["user","gL"]}],"data":{"gL":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
