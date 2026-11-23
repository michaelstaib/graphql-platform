# Defer_Should_FailTheFragmentWithTheError_When_OnlyTheLookupOfTheFragmentFails

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":123.456}},"pending":[{"id":"0","path":["product"],"label":"d"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["product","package","height"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=2, C=1
```
