# Execute_Should_ReportTheSubFieldErrorAtItsPathInEveryOrder_When_TheKeyObjectIsReturned

## intact list

```text
data: {"userList":[{"info":{"code":null},"total":98},{"info":{"code":"code-2"},"total":2}]}
errors: [{"message":"code failed for 98","path":["userList",0,"info","code"]}]

```

## nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"code failed for 98","path":["userList",0,"info","code"]}]

```

## nullable items

```text
data: {"nullableUsers":[{"info":{"code":null},"total":98,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"code failed for 98","path":["nullableUsers",0,"info","code"]}]

```
