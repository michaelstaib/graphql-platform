# Defer_Should_SkipTheLookupOfTheSwitchThatIsOn_When_SiblingSwitchesSelectTheSameFields

## Payload 1

```json
{"data":{"product":{"name":"s","price":0}},"hasNext":false}
```

## Requests

```text
deferred: A=1, B=1
plain twin: A=1, B=1
```
