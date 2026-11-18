# Defer_Should_FetchTheListOnce_When_ANestedFragmentSelectsAListOfTheOutsideSelection

## Payload 1

```json
{"data":{"products":[{"name":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"name":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"name":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}]},"pending":[{"id":"0","path":[],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["products",0],"data":{"price":0}},{"id":"0","subPath":["products",0,"reviews",0],"data":{"stars":0}},{"id":"0","subPath":["products",0,"reviews",1],"data":{"stars":0}},{"id":"0","subPath":["products",0,"reviews",2],"data":{"stars":0}},{"id":"0","subPath":["products",1],"data":{"price":0}},{"id":"0","subPath":["products",1,"reviews",0],"data":{"stars":0}},{"id":"0","subPath":["products",1,"reviews",1],"data":{"stars":0}},{"id":"0","subPath":["products",1,"reviews",2],"data":{"stars":0}},{"id":"0","subPath":["products",2],"data":{"price":0}},{"id":"0","subPath":["products",2,"reviews",0],"data":{"stars":0}},{"id":"0","subPath":["products",2,"reviews",1],"data":{"stars":0}},{"id":"0","subPath":["products",2,"reviews",2],"data":{"stars":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=1
plain twin: A=1, B=1, C=1
```
