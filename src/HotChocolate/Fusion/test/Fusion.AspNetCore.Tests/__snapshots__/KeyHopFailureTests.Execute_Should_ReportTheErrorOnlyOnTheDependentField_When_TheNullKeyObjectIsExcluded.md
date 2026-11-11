# Execute_Should_ReportTheErrorOnlyOnTheDependentField_When_TheNullKeyObjectIsExcluded

## grouping, field, s: false

```text
data: {"userList":[{"total":null},{"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## grouping, field, s: true

```text
data: {"userList":[{"total":null,"info":null},{"total":2,"info":{"sku":"sku-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## grouping, fragment, s: false

```text
data: {"userList":[{"total":null},{"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## no grouping, field, s: false

```text
data: {"userList":[{"total":null},{"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## no grouping, field, s: true

```text
data: {"userList":[{"total":null,"info":null},{"total":2,"info":{"sku":"sku-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## no grouping, fragment, s: false

```text
data: {"userList":[{"total":null},{"total":2}]}
errors: [{"message":"info failed for 98","path":["userList",0,"total"]}]

```
