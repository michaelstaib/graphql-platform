# Execute_Should_ReportTheErrorOnlyOnTheSubFieldThatRaisedItInEveryOrder_When_SiblingsAreFetched

## info { code2 code label } total, intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code2":"code2-2","code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"code failed for 98","path":["userList",0,"info","code"]},{"message":"code failed for 98","path":["userList",0,"total"]}]

```

## info { code2 code label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"code failed for 98","path":["userList",0,"info","code"]},{"message":"code failed for 98","path":["userList",0,"total"]}]

```

## info { code2 code label } total, nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"code failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"code failed for 98","path":["nullableUsers",0,"total"]}]

```
