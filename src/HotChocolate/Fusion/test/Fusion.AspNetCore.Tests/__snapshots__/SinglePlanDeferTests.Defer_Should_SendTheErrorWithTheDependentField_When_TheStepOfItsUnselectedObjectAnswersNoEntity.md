# Defer_Should_SendTheErrorWithTheDependentField_When_TheStepOfItsUnselectedObjectAnswersNoEntity

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","cA":null}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","cB"]}],"data":{"cB":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
