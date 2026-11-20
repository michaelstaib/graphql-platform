# Defer_Should_SendTheErrorWithTheDependentField_When_AnEntityOfItsRequiredListLacksALeaf

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inners":[{"c":"Inner: SW5uZXI6OTEwODM1NDEx"},{"c":"Inner: SW5uZXI6MTk4MDk0NTk4Mg=="},{"c":"Inner: SW5uZXI6MzAzMzYyMTU1NA=="}]}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"F failed 0","path":["user","gO"]}],"data":{"gO":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, F=1
```
