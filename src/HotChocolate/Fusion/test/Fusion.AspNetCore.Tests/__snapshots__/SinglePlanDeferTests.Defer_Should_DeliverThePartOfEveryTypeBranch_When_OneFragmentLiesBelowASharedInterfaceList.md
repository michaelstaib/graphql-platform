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

## Requests

```text
deferred: A=2, B=3
per type twin: A=2, B=2
```
