# Execute_Should_KeepTheSubFieldsOfTheOtherSourceInEveryOrder_When_TheLookupRaises

## info { code label } total, intact list

```text
data: {"userList":[{"info":{"code":null,"label":"label-98"},"total":null},{"info":{"code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"B lookup failed for 98","path":["userList",0,"info","code"]},{"message":"B lookup failed for 98","path":["userList",0,"total"]}]

```

## info { code label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"B lookup failed for 98","path":["userList",0,"info","code"]},{"message":"B lookup failed for 98","path":["userList",0,"total"]},{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

```

## info { code label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"code":null,"label":"label-98"},"total":null,"badNN":98},null]}
errors: [{"message":"B lookup failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"B lookup failed for 98","path":["nullableUsers",0,"total"]},{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]}]

```
