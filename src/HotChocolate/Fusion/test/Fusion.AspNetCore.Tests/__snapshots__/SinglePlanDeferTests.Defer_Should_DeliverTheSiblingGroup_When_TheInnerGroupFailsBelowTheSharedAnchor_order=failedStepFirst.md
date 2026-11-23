# Defer_Should_DeliverTheSiblingGroup_When_TheInnerGroupFailsBelowTheSharedAnchor

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"b"},{"id":"1","path":[],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","subPath":["product"],"data":{"author":{"bio":"Author: QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","reviews"]}]}],"hasNext":false}
```

## Requests

```text
A=2, B=2, C=2, D=1
```
