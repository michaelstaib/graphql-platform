# Defer_Should_DeliverThePartOfEveryTypeBranch_When_TheFragmentSelectsAnOwnFieldAndAPart

## Merged data

```json
{"feed":[{"author":{"email":"s","friends":[{"id":"s","name":"s"},{"id":"s","name":"s"},{"id":"s","name":"s"}],"name":"s"}},{"author":{"email":"s","friends":[{"id":"s","name":"s"},{"id":"s","name":"s"},{"id":"s","name":"s"}],"name":"s"}},{"author":{"email":"s","friends":[{"id":"s","name":"s"},{"id":"s","name":"s"},{"id":"s","name":"s"}],"name":"s"}}]}
```

## Announced places

```text
["feed",0,"author"] d
["feed",1,"author"] d
["feed",2,"author"] d
```

## Payload 1

```json
{"data":{"feed":[{"author":{"name":"s","friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"author":{"name":"s","friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"author":{"name":"s","friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}}]},"pending":[{"id":"0","path":["feed",0,"author"],"label":"d"},{"id":"1","path":["feed",2,"author"],"label":"d"},{"id":"2","path":["feed",1,"author"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"2","data":{"email":"s"}},{"id":"2","subPath":["friends",0],"data":{"name":"s"}},{"id":"2","subPath":["friends",1],"data":{"name":"s"}},{"id":"2","subPath":["friends",2],"data":{"name":"s"}}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"email":"s"}},{"id":"0","subPath":["friends",0],"data":{"name":"s"}},{"id":"0","subPath":["friends",1],"data":{"name":"s"}},{"id":"0","subPath":["friends",2],"data":{"name":"s"}},{"id":"1","data":{"email":"s"}},{"id":"1","subPath":["friends",0],"data":{"name":"s"}},{"id":"1","subPath":["friends",1],"data":{"name":"s"}},{"id":"1","subPath":["friends",2],"data":{"name":"s"}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=4, B=3
per type twin: A=2, B=1
```
