# Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition

## Delivery

```text
initial {"user":{"friends":[{"id":"s"},{"id":"s"},{"id":"s"}]}}
completed c
incremental c subPath [0] {"name":"s"}
incremental c subPath [1] {"name":"s"}
incremental c subPath [2] {"name":"s"}
pending c at ["user","friends"]
```

## Requests

```text
deferred: A=2, E=1
plain twin: A=1, E=1
```
