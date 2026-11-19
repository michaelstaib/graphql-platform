# Defer_Should_SendTheValue_When_TheRequestOfTheDeepestNestedFragmentAnswersFirst

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product"],"label":"x"},{"id":"3","path":["product"],"label":"y"}],"incremental":[{"id":"1","data":{"author":{"bio":"s"}}},{"id":"2","subPath":["author"],"data":{"age":0}},{"id":"3","subPath":["author"],"data":{"note":"s"}}],"completed":[{"id":"1"},{"id":"2"},{"id":"3"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","subPath":["author"],"data":{"id":"s"}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=4
plain twin: A=1, B=1
```
