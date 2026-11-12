# Execute_Should_ReportTheErrorAtTheFieldAndOnTheDependent_When_TheLookupFailsAtASelectedField

## { user(id: 2) { soft total label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]},{"message":"soft failed for 2","path":["user","total"]}],"data":{"user":{"soft":null,"total":null,"label":"label-2"}}}
```

## { user(id: 2) { total soft label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]},{"message":"soft failed for 2","path":["user","total"]}],"data":{"user":{"total":null,"soft":null,"label":"label-2"}}}
```
