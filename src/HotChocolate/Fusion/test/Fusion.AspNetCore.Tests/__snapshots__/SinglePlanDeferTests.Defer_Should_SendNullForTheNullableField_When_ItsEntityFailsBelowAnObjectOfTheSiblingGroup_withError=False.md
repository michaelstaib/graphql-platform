# Defer_Should_SendNullForTheNullableField_When_ItsEntityFailsBelowAnObjectOfTheSiblingGroup

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","subPath":["author"],"data":{"note":null}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=2
```
