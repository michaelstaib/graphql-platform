# Execute_Should_ReportTheErrorOnTheNearestFetchedFieldInEveryOrder_When_ItsPathIsNotSelected

## info { code meta { a b } detail { d } }, intact list

```text
data: {"userList":[{"info":{"code":"code-98","meta":{"a":"a-98","b":"b-98"},"detail":{"d":"d-98"}}},{"info":{"code":"code-2","meta":{"a":"a-2","b":"b-2"},"detail":{"d":"d-2"}}}]}
errors: [{"message":"detail hidden failed for 98","path":["userList",0,"info","detail"]},{"message":"info hidden failed for 98","path":["userList",0,"info","code"]},{"message":"meta hidden failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { code meta { a b } detail { d } }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"detail hidden failed for 98","path":["userList",0,"info","detail"]},{"message":"info hidden failed for 98","path":["userList",0,"info","code"]},{"message":"meta hidden failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { code meta { a b } detail { d } }, nullable items

```text
data: {"nullableUsers":[{"info":{"code":"code-98","meta":{"a":"a-98","b":"b-98"},"detail":{"d":"d-98"}},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"detail hidden failed for 98","path":["nullableUsers",0,"info","detail"]},{"message":"info hidden failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"meta hidden failed for 98","path":["nullableUsers",0,"info","meta","a"]}]

```

## info { meta { b a } code }, intact list

```text
data: {"userList":[{"info":{"meta":{"b":"b-98","a":"a-98"},"code":"code-98"}},{"info":{"meta":{"b":"b-2","a":"a-2"},"code":"code-2"}}]}
errors: [{"message":"info hidden failed for 98","path":["userList",0,"info","meta","a"]},{"message":"meta hidden failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { meta { b a } code }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info hidden failed for 98","path":["userList",0,"info","meta","a"]},{"message":"meta hidden failed for 98","path":["userList",0,"info","meta","a"]}]

```

## info { meta { b a } code }, nullable items

```text
data: {"nullableUsers":[{"info":{"meta":{"b":"b-98","a":"a-98"},"code":"code-98"},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info hidden failed for 98","path":["nullableUsers",0,"info","meta","a"]},{"message":"meta hidden failed for 98","path":["nullableUsers",0,"info","meta","a"]}]

```
