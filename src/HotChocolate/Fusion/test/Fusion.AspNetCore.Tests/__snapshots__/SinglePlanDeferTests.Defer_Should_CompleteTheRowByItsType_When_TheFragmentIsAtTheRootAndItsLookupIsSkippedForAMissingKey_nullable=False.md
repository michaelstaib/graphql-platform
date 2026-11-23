# Defer_Should_CompleteTheRowByItsType_When_TheFragmentIsAtTheRootAndItsLookupIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":[],"label":"o"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","author","bio"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=2, B=1
```
