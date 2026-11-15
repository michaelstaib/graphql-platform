# Defer_Should_FetchTheDependentField_When_TheKeyListIsSelectedByASiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"reviews":[{"id":"s"},{"id":"s"},{"id":"s"}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":["reviews",0],"data":{"rating":0}},{"id":"1","subPath":["reviews",1],"data":{"rating":0}},{"id":"1","subPath":["reviews",2],"data":{"rating":0}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=1
plain twin: A=1, B=1, C=1
```
