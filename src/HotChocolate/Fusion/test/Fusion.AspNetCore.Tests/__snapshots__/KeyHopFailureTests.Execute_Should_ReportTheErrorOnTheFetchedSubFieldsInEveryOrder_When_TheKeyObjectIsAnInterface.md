# Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_TheKeyObjectIsAnInterface

## intact list

```text
data: {"userList":[{"info":null},{"info":{"code":"code-2","label":"label-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```
