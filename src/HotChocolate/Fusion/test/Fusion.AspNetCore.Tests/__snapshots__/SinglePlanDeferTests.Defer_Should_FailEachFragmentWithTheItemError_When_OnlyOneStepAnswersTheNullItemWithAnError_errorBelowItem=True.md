# Defer_Should_FailEachFragmentWithTheItemError_When_OnlyOneStepAnswersTheNullItemWithAnError

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":["product"],"label":"c"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"1","errors":[{"message":"C failed 0","path":["product","reviews",0,"body"]}]}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"C failed 0","path":["product","reviews",0,"body"]}]}],"hasNext":false}
```

## Requests

```text
A=1, C=2
```
