# Defer_Should_FetchTheRequiredFieldOfAThirdSchema_When_TheSwitchIsOn

## Delivery

```text
initial {"product":{"name":"s"}}
completed shipping
incremental shipping subPath [] {"shipping":0}
pending shipping at ["product"]
```

## Requests

```text
deferred: A=1, B=1, C=1
plain twin: A=1, B=1, C=1
```
