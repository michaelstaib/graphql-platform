# Execute_Should_ReportTheErrorOnlyOnTheNestedLeafInEveryOrder_When_TheValueTypeHasAFieldOfItsName

## info { left { x } right { y } x }, intact list

```text
data: {"userList":[{"info":{"left":null,"right":null,"x":"info-x-98"}},{"info":{"left":{"x":"x-2"},"right":{"y":"y-2"},"x":"info-x-2"}}]}
errors: [{"message":"info failed for 98","path":["userList",0,"info","left","x"]},{"message":"info failed for 98","path":["userList",0,"info","right","y"]}]

```

## info { left { x } right { y } x }, nulled list

```text
data: {"userList":null}
errors: [{"message":"I hop failed for 93","path":["userList",1,"badNN"]},{"message":"info failed for 98","path":["userList",0,"info","left","x"]},{"message":"info failed for 98","path":["userList",0,"info","right","y"]}]

```

## info { left { x } right { y } x }, nullable items

```text
data: {"nullableUsers":[{"info":{"left":null,"right":null,"x":"info-x-98"},"badNN":98},null]}
errors: [{"message":"I hop failed for 93","path":["nullableUsers",1,"badNN"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","left","x"]},{"message":"info failed for 98","path":["nullableUsers",0,"info","right","y"]}]

```
