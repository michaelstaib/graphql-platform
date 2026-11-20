# Defer_Should_DeliverThePartOfEveryTypeBranch_When_OneFragmentLiesBelowASharedInterfaceField

## Merged data

```json
{"feed":[{"author":{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}},{"author":{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}},{"author":{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}}]}
```

## Announced places

```text
["feed",0,"author"] n
["feed",1,"author"] n
["feed",2,"author"] n
```

## Payload 1

```json
{"data":{"feed":[{"author":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"author":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},{"author":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}}]},"pending":[{"id":"0","path":["feed",0,"author"],"label":"n"},{"id":"1","path":["feed",2,"author"],"label":"n"},{"id":"2","path":["feed",1,"author"],"label":"n"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["friends",0],"data":{"email":"s"}},{"id":"0","subPath":["friends",1],"data":{"email":"s"}},{"id":"0","subPath":["friends",2],"data":{"email":"s"}},{"id":"1","subPath":["friends",0],"data":{"email":"s"}},{"id":"1","subPath":["friends",1],"data":{"email":"s"}},{"id":"1","subPath":["friends",2],"data":{"email":"s"}}],"completed":[{"id":"0"},{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","subPath":["friends",0],"data":{"email":"s"}},{"id":"2","subPath":["friends",1],"data":{"email":"s"}},{"id":"2","subPath":["friends",2],"data":{"email":"s"}}],"completed":[{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=3
per type twin: A=2, B=2
```
