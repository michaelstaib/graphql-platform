# Execute_Should_ReportTheErrorOnlyOnTheDependentFieldInEveryOrder_When_ARequirementRaises

## info { code } total, intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code":"code-2"},"total":2}]}
errors: [{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code } total, nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku failed for 98","path":["nullableUsers",0,"total"]}]

```

## info { code label } total, intact list

```text
data: {"userList":[{"info":{"code":null,"label":"label-98"},"total":null},{"info":{"code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"code":null,"label":"label-98"},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku failed for 98","path":["nullableUsers",0,"total"]}]

```

## info { code sku } total, intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code":"code-2","sku":"sku-2"},"total":2}]}
errors: [{"message":"sku failed for 98","path":["userList",0,"info","sku"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code sku } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku failed for 98","path":["userList",0,"info","sku"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code sku } total, nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku failed for 98","path":["nullableUsers",0,"info","sku"]},{"message":"sku failed for 98","path":["nullableUsers",0,"total"]}]

```
