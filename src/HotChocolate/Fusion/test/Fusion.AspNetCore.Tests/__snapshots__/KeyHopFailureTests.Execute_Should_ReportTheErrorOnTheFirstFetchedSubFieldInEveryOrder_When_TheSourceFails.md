# Execute_Should_ReportTheErrorOnTheFirstFetchedSubFieldInEveryOrder_When_TheSourceFails

## info { code label } total, intact list

```text
data: {"userList":[{"info":{"code":null,"label":"label-98"},"total":null},{"info":{"code":null,"label":"label-2"},"total":null}]}
errors: [{"message":"Unexpected Execution Error","path":["userList",0,"info","code"]},{"message":"Unexpected Execution Error","path":["userList",0,"total"]},{"message":"Unexpected Execution Error","path":["userList",1,"info","code"]},{"message":"Unexpected Execution Error","path":["userList",1,"total"]}]

```

## info { code label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"Unexpected Execution Error","path":["userList",0,"info","code"]},{"message":"Unexpected Execution Error","path":["userList",0,"total"]},{"message":"Unexpected Execution Error","path":["userList",1,"info","code"]},{"message":"Unexpected Execution Error","path":["userList",1,"total"]}]

```

## info { code label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"code":null,"label":"label-98"},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"Unexpected Execution Error","path":["nullableUsers",0,"info","code"]},{"message":"Unexpected Execution Error","path":["nullableUsers",0,"total"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"info","code"]},{"message":"Unexpected Execution Error","path":["nullableUsers",1,"total"]}]

```
