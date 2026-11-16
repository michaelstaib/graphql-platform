# Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition

## Payload 1

```json
{"data":{"user":{}},"pending":[{"id":"0","path":["user"],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["user","friends",0],"label":"c"},{"id":"2","path":["user","friends",1],"label":"c"},{"id":"3","path":["user","friends",2],"label":"c"}],"incremental":[{"id":"0","data":{"friends":[{},{},{}]}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"name":"s"}},{"id":"2","data":{"name":"s"}},{"id":"3","data":{"name":"s"}}],"completed":[{"id":"1"},{"id":"2"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
deferred: A=2, E=1
plain twin: A=1, E=1
```
