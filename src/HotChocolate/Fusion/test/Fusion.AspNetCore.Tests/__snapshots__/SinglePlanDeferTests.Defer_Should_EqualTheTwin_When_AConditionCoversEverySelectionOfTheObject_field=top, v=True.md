# Defer_Should_EqualTheTwin_When_AConditionCoversEverySelectionOfTheObject

## Payload 1

```json
{"data":{"shelf":{"top":{"upc":"s","name":"s"}}},"pending":[{"id":"0","path":["shelf","top"],"label":"x"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, E=1, H=1
plain twin: A=1, E=1, H=1, R=1
```
