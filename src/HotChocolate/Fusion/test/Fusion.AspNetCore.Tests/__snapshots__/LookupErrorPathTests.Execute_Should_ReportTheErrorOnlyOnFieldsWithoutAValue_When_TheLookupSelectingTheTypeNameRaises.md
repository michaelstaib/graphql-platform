# Execute_Should_ReportTheErrorOnlyOnFieldsWithoutAValue_When_TheLookupSelectingTheTypeNameRaises

## plain

```text
{"errors":[{"message":"source read failed","path":["entities",0,"far"]}],"data":null}
```

## deferred

```text
{"data":{"entities":[{"id":"1"},{"id":"2"}]},"pending":[{"id":"0","path":["entities"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"source read failed","path":["entities",0,"far"]}]}],"hasNext":false}

```
