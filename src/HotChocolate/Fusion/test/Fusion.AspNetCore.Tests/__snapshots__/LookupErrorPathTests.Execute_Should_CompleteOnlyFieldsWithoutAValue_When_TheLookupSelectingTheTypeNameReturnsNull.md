# Execute_Should_CompleteOnlyFieldsWithoutAValue_When_TheLookupSelectingTheTypeNameReturnsNull

## plain

```text
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["entities",0,"far"],"extensions":{"code":"HC0018"}}],"data":null}
```

## deferred

```text
{"data":{"entities":[{"id":"1"},{"id":"2"}]},"pending":[{"id":"0","path":["entities"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["entities",0,"far"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}

```
