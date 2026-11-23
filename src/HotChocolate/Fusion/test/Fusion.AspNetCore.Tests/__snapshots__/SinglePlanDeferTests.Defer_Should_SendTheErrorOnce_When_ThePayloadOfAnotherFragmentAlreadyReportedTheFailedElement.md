# Defer_Should_SendTheErrorOnce_When_ThePayloadOfAnotherFragmentAlreadyReportedTheFailedElement

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy"}},"pending":[{"id":"0","path":["user"],"label":"p"},{"id":"1","path":["user"],"label":"q"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed 1","path":["user","inners",1,"a"]}],"data":{"inners":[{"a":"Inner: SW5uZXI6OTEwODM1NDEx"},null,{"a":"Inner: SW5uZXI6MzAzMzYyMTU1NA=="}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"gL":"User: VXNlcjoyNDgxMTg2ODQy"}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1
```
