# Execute_Should_NeverReportTheErrorAtTheTypename_When_TheKeyObjectSelectsIt

## info { __typename code label }, intact list

```text
data: {"userList":[{"info":null},{"info":{"__typename":"Info","code":"code-2","label":"label-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { __typename code label }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code label __typename }, intact list

```text
data: {"userList":[{"info":null},{"info":{"code":"code-2","label":"label-2","__typename":"Info"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code label __typename }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code label }, intact list

```text
data: {"userList":[{"info":null},{"info":{"code":"code-2","label":"label-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code label }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { __typename label } total, intact list

```text
data: {"userList":[{"info":{"__typename":"Info","label":"label-98"},"total":null},{"info":{"__typename":"Info","label":"label-2"},"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { __typename label } total, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## info { __typename label } total, nullable items

```text
data: {"nullableUsers":[{"info":{"__typename":"Info","label":"label-98"},"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

```
