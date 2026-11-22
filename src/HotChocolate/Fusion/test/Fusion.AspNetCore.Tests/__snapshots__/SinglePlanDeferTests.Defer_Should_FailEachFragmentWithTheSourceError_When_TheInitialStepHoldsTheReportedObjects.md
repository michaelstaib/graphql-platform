# Defer_Should_FailEachFragmentWithTheSourceError_When_TheInitialStepHoldsTheReportedObjects

## Payload 1

```json
{"errors":[{"message":"B failed","path":["users",0,"inner"]},{"message":"B failed","path":["users",1,"inner"]},{"message":"B failed","path":["users",2,"inner"]}],"data":{"users":[{"nick":"User: VXNlcjoyNjE2NDgyNzU=","inner":null},{"nick":"User: VXNlcjoxNTU5MzY4MDMx","inner":null},{"nick":"User: VXNlcjozNDQ5MTc0MTI0","inner":null}]},"pending":[{"id":"0","path":["users",0]},{"id":"1","path":["users",1]},{"id":"2","path":["users",2]}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"B failed","path":["users",0,"nnA"]}]},{"id":"1","errors":[{"message":"B failed","path":["users",1,"nnA"]}]},{"id":"2","errors":[{"message":"B failed","path":["users",2,"nnA"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
