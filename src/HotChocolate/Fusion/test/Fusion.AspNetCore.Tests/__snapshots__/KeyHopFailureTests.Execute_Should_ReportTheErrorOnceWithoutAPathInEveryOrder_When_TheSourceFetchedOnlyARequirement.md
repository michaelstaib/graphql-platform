# Execute_Should_ReportTheErrorOnceWithoutAPathInEveryOrder_When_TheSourceFetchedOnlyARequirement

## info { label } total, intact list

```text
data: {"userList":[{"info":{"label":"label-98"},"total":98},{"info":{"label":"label-2"},"total":2}]}
errors: [{"message":"sku hidden failed for 98"}]

```

## info { label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku hidden failed for 98"}]

```

## info { label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"label":"label-98"},"total":98,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku hidden failed for 98"}]

```
