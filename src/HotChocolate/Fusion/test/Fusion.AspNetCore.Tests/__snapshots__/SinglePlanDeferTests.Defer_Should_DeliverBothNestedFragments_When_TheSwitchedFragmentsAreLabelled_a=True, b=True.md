# Defer_Should_DeliverBothNestedFragments_When_TheSwitchedFragmentsAreLabelled

## Payload 1

```json
{"data":{"product":{"name":"s"}},"pending":[{"id":"0","path":["product"],"label":"p"},{"id":"1","path":["product"],"label":"q"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"2","path":["product","author"]},{"id":"3","path":["product","author"]}],"incremental":[{"id":"0","data":{"author":{}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"pending":[{"id":"4","path":["product","author"]}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 4

```json
{"incremental":[{"id":"2","data":{"bio":"s"}},{"id":"3","data":{"nick":"s"}}],"completed":[{"id":"2"},{"id":"3"},{"id":"4"}],"hasNext":false}
```

## Requests

```text
deferred: A=1, B=2, C=1
plain twin: A=1, B=1, C=1
```
