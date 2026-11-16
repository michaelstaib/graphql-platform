# Defer_Should_KeepTheFirstResult_When_ALaterAnswerOfTheSharedFieldIsAMissingEntity

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"weight":null}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"shipping":123.456}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, S=1, W=2
```
