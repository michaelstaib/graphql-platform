# Defer_Should_DeliverTheFieldsOfEveryItem_When_TheFragmentHoldsTwoTypeConditionsBelowAListInAList

## Payload 1

```json
{"data":{"current":{"petGroups":[[{},{}],[{}],[{},{}]]}},"pending":[{"id":"0","path":["current","petGroups",0,0]},{"id":"1","path":["current","petGroups",0,1]},{"id":"2","path":["current","petGroups",1,0]},{"id":"3","path":["current","petGroups",2,0]},{"id":"4","path":["current","petGroups",2,1]}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"bark":"bark-5","owner":{"name":"name-5"}}},{"id":"1","data":{"meow":"meow-6"}},{"id":"2","data":{"bark":"bark-7","owner":{"name":"name-7"}}},{"id":"3","data":{"meow":"meow-8"}},{"id":"4","data":{"bark":"bark-9","owner":{"name":"name-9"}}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, E=1, P=1
plain twin: A=1, E=1, P=1
```
