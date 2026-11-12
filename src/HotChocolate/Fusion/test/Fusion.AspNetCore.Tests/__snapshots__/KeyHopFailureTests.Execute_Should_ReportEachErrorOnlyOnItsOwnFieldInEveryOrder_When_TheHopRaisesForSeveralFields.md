# Execute_Should_ReportEachErrorOnlyOnItsOwnFieldInEveryOrder_When_TheHopRaisesForSeveralFields

## 98, intact list

```text
data: {"userList":[{"info":{"code":"code-98","label":"label-98"},"region":null},{"info":{"code":"code-2","label":"label-2"},"region":"eu"}]}
errors: [{"message":"region failed for 98","path":["userList",0,"region"]}]

```

## 98, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"region failed for 98","path":["userList",0,"region"]}]

```

## 97, intact list

```text
data: {"userList":[{"info":null,"region":null},{"info":{"code":"code-2","label":"label-2"},"region":"eu"}]}
errors: [{"message":"info failed for 97","path":["userList",0,"info","code"]},{"message":"region failed for 97","path":["userList",0,"region"]}]

```

## 97, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 97","path":["userList",0,"info","code"]},{"message":"region failed for 97","path":["userList",0,"region"]}]

```
