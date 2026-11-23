# Defer_Should_AnnounceTheNestedFragment_When_ItsStepAnswersNothingAfterTheOuterOneSendsTheAuthor

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"pending":[{"id":"1","path":["product","author"],"label":"c"}],"incremental":[{"id":"0","data":{"author":{"id":"QXV0aG9yOjIxMDQ5MzAzMTE="}}}],"completed":[{"id":"0"}],"hasNext":true}
```

## Payload 3

```json
{"incremental":[{"id":"1","data":{"bio":null}}],"completed":[{"id":"1"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
