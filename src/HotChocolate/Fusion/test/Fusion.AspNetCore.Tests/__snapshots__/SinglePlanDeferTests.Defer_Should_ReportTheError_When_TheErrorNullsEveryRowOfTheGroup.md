# Defer_Should_ReportTheError_When_TheErrorNullsEveryRowOfTheGroup

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products"],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["products",0,"price"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
