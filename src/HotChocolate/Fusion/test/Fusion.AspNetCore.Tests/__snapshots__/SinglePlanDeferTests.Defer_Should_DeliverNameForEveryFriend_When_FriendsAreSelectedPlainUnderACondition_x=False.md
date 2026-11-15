# Defer_Should_DeliverNameForEveryFriend_When_FriendsAreSelectedPlainUnderACondition

## Delivery

```text
initial {"user":{}}
completed c
completed o
incremental c subPath [0] {"name":"s"}
incremental c subPath [1] {"name":"s"}
incremental c subPath [2] {"name":"s"}
incremental o subPath [] {"friends":[{},{},{}]}
pending c at ["user","friends"]
pending o at ["user"]
```

## Requests

```text
deferred: A=2, E=1
plain twin: A=1, E=1
```
