# Execute_Should_ReportTheErrorOnTheFetchedSubFieldsInEveryOrder_When_ASiblingNullsTheList

## code and label

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## label

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```
