# Defer_Should_DeliverThePartOfEveryTypeBranch_When_OneFragmentLiesBelowASharedInterfaceList

## Merged data

```json
{"feed":[{"reviewers":[{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}]},{"reviewers":[{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}]},{"reviewers":[{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]},{"friends":[{"email":"s","id":"s"},{"email":"s","id":"s"},{"email":"s","id":"s"}]}]}]}
```

## Announced places

```text
["feed",0,"reviewers",0] n
["feed",0,"reviewers",1] n
["feed",0,"reviewers",2] n
["feed",1,"reviewers",0] n
["feed",1,"reviewers",1] n
["feed",1,"reviewers",2] n
["feed",2,"reviewers",0] n
["feed",2,"reviewers",1] n
["feed",2,"reviewers",2] n
```

## Payload 1

```json
{"data":{"feed":[{"reviewers":[{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}]},{"reviewers":[{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}]},{"reviewers":[{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]},{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}]}]},"pending":[{"id":"0","path":["feed",0,"reviewers",0],"label":"n"},{"id":"1","path":["feed",0,"reviewers",1],"label":"n"},{"id":"2","path":["feed",0,"reviewers",2],"label":"n"},{"id":"3","path":["feed",2,"reviewers",0],"label":"n"},{"id":"4","path":["feed",2,"reviewers",1],"label":"n"},{"id":"5","path":["feed",2,"reviewers",2],"label":"n"},{"id":"6","path":["feed",1,"reviewers",0],"label":"n"},{"id":"7","path":["feed",1,"reviewers",1],"label":"n"},{"id":"8","path":["feed",1,"reviewers",2],"label":"n"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["friends",0],"data":{"email":"s"}},{"id":"0","subPath":["friends",1],"data":{"email":"s"}},{"id":"0","subPath":["friends",2],"data":{"email":"s"}},{"id":"1","subPath":["friends",0],"data":{"email":"s"}},{"id":"1","subPath":["friends",1],"data":{"email":"s"}},{"id":"1","subPath":["friends",2],"data":{"email":"s"}},{"id":"2","subPath":["friends",0],"data":{"email":"s"}},{"id":"2","subPath":["friends",1],"data":{"email":"s"}},{"id":"2","subPath":["friends",2],"data":{"email":"s"}},{"id":"3","subPath":["friends",0],"data":{"email":"s"}},{"id":"3","subPath":["friends",1],"data":{"email":"s"}},{"id":"3","subPath":["friends",2],"data":{"email":"s"}},{"id":"4","subPath":["friends",0],"data":{"email":"s"}},{"id":"4","subPath":["friends",1],"data":{"email":"s"}},{"id":"4","subPath":["friends",2],"data":{"email":"s"}},{"id":"5","subPath":["friends",0],"data":{"email":"s"}},{"id":"5","subPath":["friends",1],"data":{"email":"s"}},{"id":"5","subPath":["friends",2],"data":{"email":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"},{"id":"3"},{"id":"4"},{"id":"5"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"6","subPath":["friends",0],"data":{"email":"s"}},{"id":"6","subPath":["friends",1],"data":{"email":"s"}},{"id":"6","subPath":["friends",2],"data":{"email":"s"}},{"id":"7","subPath":["friends",0],"data":{"email":"s"}},{"id":"7","subPath":["friends",1],"data":{"email":"s"}},{"id":"7","subPath":["friends",2],"data":{"email":"s"}},{"id":"8","subPath":["friends",0],"data":{"email":"s"}},{"id":"8","subPath":["friends",1],"data":{"email":"s"}},{"id":"8","subPath":["friends",2],"data":{"email":"s"}}],"completed":[{"id":"6"},{"id":"7"},{"id":"8"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, B=3
per type twin: A=2, B=2
```
