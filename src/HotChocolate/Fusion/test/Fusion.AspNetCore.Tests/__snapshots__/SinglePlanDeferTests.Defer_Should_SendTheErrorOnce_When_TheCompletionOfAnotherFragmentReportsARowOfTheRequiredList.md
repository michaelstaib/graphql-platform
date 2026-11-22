# Defer_Should_SendTheErrorOnce_When_TheCompletionOfAnotherFragmentReportsARowOfTheRequiredList

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"]},{"id":"1","path":["user"],"label":"q"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","errors":[{"message":"B failed 0","path":["user","inners",0]}],"data":{"inners":[{"c":"Inner: SW5uZXI6OTEwODM1NDEx"},{"c":"Inner: SW5uZXI6MTk4MDk0NTk4Mg=="},{"c":"Inner: SW5uZXI6MzAzMzYyMTU1NA=="}]}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"gL":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
