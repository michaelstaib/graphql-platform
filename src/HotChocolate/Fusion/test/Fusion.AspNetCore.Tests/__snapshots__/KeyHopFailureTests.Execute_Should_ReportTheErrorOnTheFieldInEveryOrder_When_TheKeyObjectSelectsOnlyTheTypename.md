# Execute_Should_ReportTheErrorOnTheFieldInEveryOrder_When_TheKeyObjectSelectsOnlyTheTypename

## info { __typename }, intact list

```text
data: {"userList":[{"info":null},{"info":{"__typename":"InfoA"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info"]}]

```

## info { __typename }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info"]}]

```

## info { __typename }, nullable items

```text
data: {"nullableUsers":[{"info":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info"]}]

```

## info { __typename ... on InfoA { label } ... on InfoB { code2 } }, intact list

```text
data: {"userList":[{"info":null},{"info":{"__typename":"InfoA","label":"label-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info"]}]

```

## info { __typename ... on InfoA { label } ... on InfoB { code2 } }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info"]}]

```

## info { __typename ... on InfoA { label } ... on InfoB { code2 } }, nullable items

```text
data: {"nullableUsers":[{"info":null,"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info"]}]

```

## info { __typename ... on InfoA { label } }, intact list

```text
data: {"userList":[{"info":{"__typename":"InfoA","label":"label-98"}},{"info":{"__typename":"InfoA","label":"label-2"}}]}

```

## info { __typename ... on InfoA { label } }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]}]

```

## info { __typename ... on InfoA { label } }, nullable items

```text
data: {"nullableUsers":[{"info":{"__typename":"InfoA","label":"label-98"},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]}]

```
