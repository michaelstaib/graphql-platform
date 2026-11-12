# Execute_Should_ReportOnlyTheErrorOfTheRequirementSourceOnTheDependentField_When_BothSourcesRaise

## intact list

```text
data: {"userList":[{"info":null,"total":null},{"info":{"code":"code-2","label":"label-2"},"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]},{"message":"x info failed for 98","path":["userList",0,"info","label"]}]

```

## nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","code"]},{"message":"info failed for 98","path":["userList",0,"total"]},{"message":"x info failed for 98","path":["userList",0,"info","label"]}]

```

## nullable items

```text
data: {"nullableUsers":[{"info":null,"total":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","code"]},{"message":"info failed for 98","path":["nullableUsers",0,"total"]},{"message":"x info failed for 98","path":["nullableUsers",0,"info","label"]}]

```
