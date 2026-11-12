# Execute_Should_ReportTheErrorOnceOnTheListInEveryOrder_When_TheSourceRaisesAtTheList

## intact list

```text
data: {"userList":[{"infos":null},{"infos":[{"code":"code-97-0","label":"label-97-0"},{"code":null,"label":"label-97-1"}]},{"infos":null},{"infos":[{"code":"code-2-0","label":"label-2-0"},{"code":"code-2-1","label":"label-2-1"}]}]}
errors: [{"message":"B lookup failed for 96","path":["userList",2,"infos"]},{"message":"code failed for 97","path":["userList",1,"infos",1,"code"]},{"message":"infos failed for 98","path":["userList",0,"infos"]}]

```

## nulled list

```text
data: {"userList":null}
errors: [{"message":"B lookup failed for 96","path":["userList",2,"infos"]},{"message":"I hop failed for 93","path":["userList",3,"badNN"]},{"message":"code failed for 97","path":["userList",1,"infos",1,"code"]},{"message":"infos failed for 98","path":["userList",0,"infos"]}]

```

## nullable items

```text
data: {"nullableUsers":[{"infos":null,"badNN":98},{"infos":[{"code":"code-97-0","label":"label-97-0"},{"code":null,"label":"label-97-1"}],"badNN":97},{"infos":null,"badNN":96},null]}
errors: [{"message":"B lookup failed for 96","path":["nullableUsers",2,"infos"]},{"message":"I hop failed for 93","path":["nullableUsers",3,"badNN"]},{"message":"code failed for 97","path":["nullableUsers",1,"infos",1,"code"]},{"message":"infos failed for 98","path":["nullableUsers",0,"infos"]}]

```
