# Defer_Should_SendEachEntityTheErrorOfItsOwnAnswer_When_EveryEntityLacksItsRequirement

## Payload 1

```json
{"data":{"users":[{"nick":"User: VXNlcjoyNjE2NDgyNzU=","inner":{"c":"Inner: SW5uZXI6NDA3NTcyNTgxOA=="}},{"nick":"User: VXNlcjoxNTU5MzY4MDMx","inner":{"c":"Inner: SW5uZXI6Mjg4NzEwODY2"}},{"nick":"User: VXNlcjozNDQ5MTc0MTI0","inner":{"c":"Inner: SW5uZXI6MzcwNTEyOTgzMQ=="}}]},"pending":[{"id":"0","path":["users",0]},{"id":"1","path":["users",1]},{"id":"2","path":["users",2]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed 0","path":["users",0,"cA"]}],"data":{"cA":null}},{"id":"1","errors":[{"message":"B failed 1","path":["users",1,"cA"]}],"data":{"cA":null}},{"id":"2","errors":[{"message":"B failed 2","path":["users",2,"cA"]}],"data":{"cA":null}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
