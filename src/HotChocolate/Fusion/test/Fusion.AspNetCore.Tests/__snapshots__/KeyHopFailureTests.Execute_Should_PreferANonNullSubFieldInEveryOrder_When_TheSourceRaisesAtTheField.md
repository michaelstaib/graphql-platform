# Execute_Should_PreferANonNullSubFieldInEveryOrder_When_TheSourceRaisesAtTheField

## info { opt code label } total, intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"opt":"opt-2","code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { opt code label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { opt code label } total, nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

```

## info { code opt label } total, intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code":"code-2","opt":"opt-2","label":"label-2"},"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { code opt label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { code opt label } total, nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

```

## info { opt label } total, intact list

```text
data: {"userList":[{"info":{"opt":null,"label":"label-98"},"total":null},{"info":{"opt":"opt-2","label":"label-2"},"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","opt"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { opt label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","opt"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { opt label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"opt":null,"label":"label-98"},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","opt"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

```
