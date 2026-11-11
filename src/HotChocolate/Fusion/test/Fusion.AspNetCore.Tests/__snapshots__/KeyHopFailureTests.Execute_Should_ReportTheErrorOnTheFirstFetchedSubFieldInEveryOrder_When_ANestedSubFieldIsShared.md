# Execute_Should_ReportTheErrorOnTheFirstFetchedSubFieldInEveryOrder_When_ANestedSubFieldIsShared

## info { code meta { a b } }, intact list

```text
data: {"userList":[{"info":{"code":null,"meta":{"a":null,"b":"b-98"}}},{"info":{"code":"code-2","meta":{"a":"a-2","b":"b-2"}}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code meta { a b } }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]}]

```

## info { code meta { a b } }, nullable items

```text
data: {"nullableUsers":[{"info":{"code":null,"meta":{"a":null,"b":"b-98"}},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","code"]}]

```

## info { meta { a b } code }, intact list

```text
data: {"userList":[{"info":{"meta":{"a":null,"b":"b-98"},"code":null}},{"info":{"meta":{"a":"a-2","b":"b-2"},"code":"code-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { meta { a b } code }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { meta { a b } code }, nullable items

```text
data: {"nullableUsers":[{"info":{"meta":{"a":null,"b":"b-98"},"code":null},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","meta","a"]}]

```
