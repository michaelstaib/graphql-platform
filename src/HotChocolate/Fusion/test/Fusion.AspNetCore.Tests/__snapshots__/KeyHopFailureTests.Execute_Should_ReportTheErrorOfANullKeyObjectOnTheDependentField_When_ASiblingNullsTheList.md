# Execute_Should_ReportTheErrorOfANullKeyObjectOnTheDependentField_When_ASiblingNullsTheList

## userList

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## nullableUsers

```text
data: {"nullableUsers":[{"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]}]

```
