# Defer_Should_CompleteTheRowByItsType_When_TheFragmentIsAtTheRootAndItsLookupIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":[],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"0","subPath":["product"],"data":{"author":{"bio":null}}}],"completed":[{"id":"0"}],"hasNext":false}
```

## Requests

```text
A=2, B=1
```
