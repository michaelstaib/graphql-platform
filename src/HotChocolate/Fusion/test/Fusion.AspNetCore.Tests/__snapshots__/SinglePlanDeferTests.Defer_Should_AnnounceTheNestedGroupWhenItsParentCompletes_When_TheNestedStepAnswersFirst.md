# Defer_Should_AnnounceTheNestedGroupWhenItsParentCompletes_When_TheNestedStepAnswersFirst

## Payload 1

```json
{"data":{"products":[{"name":"s"},{"name":"s"},{"name":"s"}]},"pending":[{"id":"0","path":[],"label":"outer"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["products",0],"label":"inner"},{"id":"2","path":["products",1],"label":"inner"},{"id":"3","path":["products",2],"label":"inner"}],"incremental":[{"id":"0","subPath":["products",0],"data":{"price":0}},{"id":"0","subPath":["products",1],"data":{"price":0}},{"id":"0","subPath":["products",2],"data":{"price":0}},{"id":"1","data":{"rating":0}},{"id":"2","data":{"rating":0}},{"id":"3","data":{"rating":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2
plain twin: A=1, B=1
```
