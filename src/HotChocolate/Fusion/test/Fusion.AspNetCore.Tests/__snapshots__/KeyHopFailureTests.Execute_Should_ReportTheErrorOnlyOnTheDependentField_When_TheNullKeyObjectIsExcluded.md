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

## every order, nulled list, s: false

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```

## every order, nulled list, s: true

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info"]},{"message":"info failed for 98","path":["userList",0,"total"]}]

```
