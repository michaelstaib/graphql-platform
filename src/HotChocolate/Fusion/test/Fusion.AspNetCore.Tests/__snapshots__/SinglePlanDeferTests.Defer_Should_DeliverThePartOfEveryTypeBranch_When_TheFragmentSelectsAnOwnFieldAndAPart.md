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

## Requests

```text
deferred: A=4, B=3
per type twin: A=2, B=1
```
