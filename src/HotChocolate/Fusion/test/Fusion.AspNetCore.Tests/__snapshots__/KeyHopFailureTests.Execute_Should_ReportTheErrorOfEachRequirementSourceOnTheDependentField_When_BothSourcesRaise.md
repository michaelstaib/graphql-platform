# Execute_Should_ReportTheErrorOfEachRequirementSourceOnTheDependentField_When_BothSourcesRaise

## intact list

```text
data: {"userList":[{"info":null,"region":null,"total":null},{"info":{"code":"code-2"},"region":"eu","total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info"]},{"message":"info failed for 98","path":["userList",0,"region"]},{"message":"info failed for 98","path":["userList",0,"total"]},{"message":"region failed for 98","path":["userList",0,"region"]},{"message":"region failed for 98","path":["userList",0,"total"]}]

```

## nullable items

```text
data: {"nullableUsers":[{"info":null,"region":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info"]},{"message":"info failed for 98","path":["nullableUsers",0,"region"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]},{"message":"region failed for 98","path":["nullableUsers",0,"region"]},{"message":"region failed for 98","path":["nullableUsers",0,"total"]}]

```

## errors of the dependent field in response order

```text
info failed for 98
region failed for 98

```
