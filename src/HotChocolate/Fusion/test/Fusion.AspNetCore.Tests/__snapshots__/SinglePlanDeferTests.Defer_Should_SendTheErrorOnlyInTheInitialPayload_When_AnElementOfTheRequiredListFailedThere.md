# Defer_Should_SendTheErrorOnlyInTheInitialPayload_When_AnElementOfTheRequiredListFailedThere

## Payload 1

```json
{"errors":[{"message":"B failed 1","path":["user","inners",1,"a"]}],"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inners":[{"a":"Inner: SW5uZXI6OTEwODM1NDEx","c":"Inner: SW5uZXI6OTEwODM1NDEx"},null,{"a":"Inner: SW5uZXI6MzAzMzYyMTU1NA==","c":"Inner: SW5uZXI6MzAzMzYyMTU1NA=="}]}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"gL":"User: VXNlcjoyNDgxMTg2ODQy"}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1
```
