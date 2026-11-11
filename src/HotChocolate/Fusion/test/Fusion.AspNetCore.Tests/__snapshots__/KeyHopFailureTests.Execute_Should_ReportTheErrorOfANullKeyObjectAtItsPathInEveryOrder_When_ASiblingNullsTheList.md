# Execute_Should_ReportTheErrorOfANullKeyObjectAtItsPathInEveryOrder_When_ASiblingNullsTheList

## info

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info"]}]

```

## alias

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"i"]}]

```
