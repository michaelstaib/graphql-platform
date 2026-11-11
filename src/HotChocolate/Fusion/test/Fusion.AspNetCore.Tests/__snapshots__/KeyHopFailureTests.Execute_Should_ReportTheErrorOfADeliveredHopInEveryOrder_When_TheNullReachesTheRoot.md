# Execute_Should_ReportTheErrorOfADeliveredHopInEveryOrder_When_TheNullReachesTheRoot

## sub-fields of two sources

```text
data: null
errors: [{"message":"I hop failed for 93","path":["users",1,"badNN"]},{"message":"info failed for 98","path":["users",0,"info","code"]}]

```

## every sub-field from one source

```text
data: null
errors: [{"message":"I hop failed for 93","path":["users",1,"badNN"]},{"message":"info failed for 98","path":["users",0,"info"]}]

```

## sub-fields of two sources, B answers after I

```text
data: null
errors: [{"message":"I hop failed for 93","path":["users",1,"badNN"]}]

```

## every sub-field from one source, B answers after I

```text
data: null
errors: [{"message":"I hop failed for 93","path":["users",1,"badNN"]}]

```
