# Defer_Should_SendTheSharedFieldWithTheGroupAtTheLongerPath_When_TheEntityOfOneGroupIsMissing

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"},{"id":"2","path":["product"],"label":"d"},{"id":"3","path":[],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"2","data":{"price":123.456}}],"completed":[{"id":"2"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"1","subPath":["author"],"data":{"bio":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}],"completed":[{"id":"1"},{"id":"3"}],"hasNext":false}
```

## Requests

```text
A=2, B=3, P=1
```
