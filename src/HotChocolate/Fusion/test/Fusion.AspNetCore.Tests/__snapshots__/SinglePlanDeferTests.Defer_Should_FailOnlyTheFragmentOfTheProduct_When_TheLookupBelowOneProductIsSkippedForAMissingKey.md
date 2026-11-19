# Defer_Should_FailOnlyTheFragmentOfTheProduct_When_TheLookupBelowOneProductIsSkippedForAMissingKey

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"a"},{"id":"1","path":["products",1],"label":"a"},{"id":"2","path":["products",2],"label":"a"}],"hasNext":true}
```

## Payload 2

```json
{"incremental":[{"id":"1","data":{"author":{"bio":"Author: QXV0aG9yOjEwMzI1NzQwOTA="}}},{"id":"2","data":{"author":{"bio":"Author: QXV0aG9yOjEwNTE2NTA2NDc="}}}],"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["products",0,"author","bio"],"extensions":{"code":"HC0018"}}]},{"id":"1"},{"id":"2"}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1
```
