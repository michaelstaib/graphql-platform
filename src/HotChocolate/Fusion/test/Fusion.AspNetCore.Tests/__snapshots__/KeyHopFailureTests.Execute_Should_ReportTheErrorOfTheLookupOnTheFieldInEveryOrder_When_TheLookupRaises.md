# Execute_Should_ReportTheErrorOfTheLookupOnTheFieldInEveryOrder_When_TheLookupRaises

## intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"B lookup failed for 98","path":["userList",0,"info"]},{"message":"B lookup failed for 98","path":["userList",0,"total"]}]

```

## nulled list

```text
data: {"userList":null}
errors: [{"message":"B lookup failed for 98","path":["userList",0,"info"]},{"message":"B lookup failed for 98","path":["userList",0,"total"]},{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

```

## nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"B lookup failed for 98","path":["nullableUsers",0,"info"]},{"message":"B lookup failed for 98","path":["nullableUsers",0,"total"]},{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]}]

```
