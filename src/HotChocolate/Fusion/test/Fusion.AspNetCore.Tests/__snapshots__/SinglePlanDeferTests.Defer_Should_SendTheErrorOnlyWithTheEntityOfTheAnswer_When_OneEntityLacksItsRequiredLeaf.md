# Defer_Should_SendTheErrorOnlyWithTheEntityOfTheAnswer_When_OneEntityLacksItsRequiredLeaf

## Payload 1

```json
{"data":{"users":[{"nick":"User: VXNlcjoyNjE2NDgyNzU="},{"nick":"User: VXNlcjoxNTU5MzY4MDMx"},{"nick":"User: VXNlcjozNDQ5MTc0MTI0"}]},"pending":[{"id":"0","path":["users",0]},{"id":"1","path":["users",1]},{"id":"2","path":["users",2]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"cX":"User: VXNlcjoyNjE2NDgyNzU="}},{"id":"1","errors":[{"message":"B failed","path":["users",1,"cX"]}],"data":{"cX":null}},{"id":"2","data":{"cX":"User: VXNlcjozNDQ5MTc0MTI0"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
