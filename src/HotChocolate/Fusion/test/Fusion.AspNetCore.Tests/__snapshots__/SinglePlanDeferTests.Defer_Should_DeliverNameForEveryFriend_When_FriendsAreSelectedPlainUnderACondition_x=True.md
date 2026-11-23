# Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition

## Payload 1

```json
{"data":{"user":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}},"pending":[{"id":"0","path":["user","friends",0],"label":"c"},{"id":"1","path":["user","friends",1],"label":"c"},{"id":"2","path":["user","friends",2],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"name":"s"}},{"id":"1","data":{"name":"s"}},{"id":"2","data":{"name":"s"}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, E=1
plain twin: A=1, E=1
```
