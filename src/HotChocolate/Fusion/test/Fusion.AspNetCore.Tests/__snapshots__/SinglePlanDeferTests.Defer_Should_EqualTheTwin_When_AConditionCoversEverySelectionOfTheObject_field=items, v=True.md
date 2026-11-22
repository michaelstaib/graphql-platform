# Defer_Should_EqualTheTwin_When_AConditionCoversEverySelectionOfTheObject

## Payload 1

```json
{"data":{"shelf":{"items":[{"upc":"s","name":"s"},{"upc":"s","name":"s"},{"upc":"s","name":"s"}]}},"pending":[{"id":"0","path":["shelf","items",0],"label":"x"},{"id":"1","path":["shelf","items",1],"label":"x"},{"id":"2","path":["shelf","items",2],"label":"x"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"price":0}},{"id":"1","data":{"price":0}},{"id":"2","data":{"price":0}}],"completed":[{"id":"0"},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, E=1, H=1
plain twin: A=1, E=1, H=1, R=1
```
