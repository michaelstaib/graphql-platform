# Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition

## Payload 1

```json
{"data":{"user":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},"pending":[{"id":"1","path":["user","friends"],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","subPath":[0],"data":{"name":"s"}},{"id":"1","subPath":[1],"data":{"name":"s"}},{"id":"1","subPath":[2],"data":{"name":"s"}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, E=1
plain twin: A=1, E=1
```
