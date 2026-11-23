# Defer_Should_SendTheErrorOfTheFirstValue_When_OneRequirementReadsTwoDeniedValues

## Payload 1

```json
{"data":{"user":{"nick":"User: VXNlcjoyNDgxMTg2ODQy","inner":{"c":"Inner: SW5uZXI6MzAzMTU3MjMyMQ=="},"box":{"y":"Box: Qm94OjUxMjIxMzYxNw=="}}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","errors":[{"message":"B failed","path":["user","gO"]}],"data":{"gO":null}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=2, F=2
```
