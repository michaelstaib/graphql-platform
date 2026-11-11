# Execute_Should_ReportTheSubFieldErrorAtItsPathInEveryOrder_When_TheSourceNullsANestedKeyObject

## info { code detail { sku } } total, intact list

```text
data: {"userList":[{"info":{"code":"code-98","detail":null},"total":null},{"info":{"code":"code-2","detail":{"sku":"sku-2"}},"total":2}]}
errors: [{"message":"sku failed for 98","path":["userList",0,"info","detail","sku"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code detail { sku } } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku failed for 98","path":["userList",0,"info","detail","sku"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code detail { sku } } total, nullable items

```text
data: {"nullableUsers":[{"info":{"code":"code-98","detail":null},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku failed for 98","path":["nullableUsers",0,"info","detail","sku"]},{"message":"sku failed for 98","path":["nullableUsers",0,"total"]}]

```

## info { code } total, intact list

```text
data: {"userList":[{"info":{"code":"code-98"},"total":null},{"info":{"code":"code-2"},"total":2}]}
errors: [{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"sku failed for 98","path":["userList",0,"total"]}]

```

## info { code } total, nullable items

```text
data: {"nullableUsers":[{"info":{"code":"code-98"},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"sku failed for 98","path":["nullableUsers",0,"total"]}]

```
