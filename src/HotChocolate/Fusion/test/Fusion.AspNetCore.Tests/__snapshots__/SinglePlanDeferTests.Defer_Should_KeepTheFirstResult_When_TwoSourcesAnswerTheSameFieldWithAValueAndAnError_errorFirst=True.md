# Defer_Should_KeepTheFirstResult_When_TwoSourcesAnswerTheSameFieldWithAValueAndAnError

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["product","price"]}],"data":{"price":null,"weight":123}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"0","data":{"rating":123}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
