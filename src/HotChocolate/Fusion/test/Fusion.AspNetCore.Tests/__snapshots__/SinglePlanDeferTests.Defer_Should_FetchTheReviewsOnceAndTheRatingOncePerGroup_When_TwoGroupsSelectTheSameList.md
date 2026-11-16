# Defer_Should_FetchTheReviewsOnceAndTheRatingOncePerGroup_When_TwoGroupsSelectTheSameList

## Payload 1

```json
{"data":{"products":[{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"id":"s","reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}]},"pending":[{"id":"0","path":["products",0,"reviews",0],"label":"i"},{"id":"1","path":["products",0,"reviews",1],"label":"i"},{"id":"2","path":["products",0,"reviews",2],"label":"i"},{"id":"3","path":["products",1,"reviews",0],"label":"i"},{"id":"4","path":["products",1,"reviews",1],"label":"i"},{"id":"5","path":["products",1,"reviews",2],"label":"i"},{"id":"6","path":["products",2,"reviews",0],"label":"i"},{"id":"7","path":["products",2,"reviews",1],"label":"i"},{"id":"8","path":["products",2,"reviews",2],"label":"i"},{"id":"9","path":["products",0],"label":"o"},{"id":"10","path":["products",1],"label":"o"},{"id":"11","path":["products",2],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"rating":0}},{"id":"1","data":{"rating":0}},{"id":"2","data":{"rating":0}},{"id":"3","data":{"rating":0}},{"id":"4","data":{"rating":0}},{"id":"5","data":{"rating":0}},{"id":"6","data":{"rating":0}},{"id":"7","data":{"rating":0}},{"id":"8","data":{"rating":0}}],"completed":[{"id":"9"},{"id":"10"},{"id":"11"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"},{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1, C=2
plain twin: A=1, B=1, C=1
```
