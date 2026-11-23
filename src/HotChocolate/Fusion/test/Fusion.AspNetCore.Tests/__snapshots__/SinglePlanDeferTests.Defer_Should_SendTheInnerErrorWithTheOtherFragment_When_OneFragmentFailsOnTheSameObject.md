# Defer_Should_SendTheInnerErrorWithTheOtherFragment_When_OneFragmentFailsOnTheSameObject

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE=","shipping":123.456}},"pending":[{"id":"0","path":["product"],"label":"d"},{"id":"1","path":["product"],"label":"e"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","parts",0,"weight"],"extensions":{"code":"HC0018"}}],"data":{"package":{"parts":[null,{"weight":123.456},{"weight":123.456}]}}}],"completed":[{"id":"1"}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["product","package","height"],"extensions":{"code":"HC0018"}}]}],"hasNext":false}
```

## Requests

```text
A=1, B=3, C=1
```
