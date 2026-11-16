# Defer_Should_CompleteThePendingId_When_TheMarkedStepFailsInTransport

## Payload 1

```json
{"data":{"products":[{"name":"Product: UHJvZHVjdDoxOTc0MTQ1MDk="},{"name":"Product: UHJvZHVjdDozOTM1NzEyMDU5"},{"name":"Product: UHJvZHVjdDoyMjE1MjQ2OTE2"}]},"pending":[{"id":"0","path":["products",0],"label":"p"},{"id":"1","path":["products",1],"label":"p"},{"id":"2","path":["products",2],"label":"p"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Unexpected Execution Error","path":["products",0,"price"]}]},{"id":"1","errors":[{"message":"Unexpected Execution Error","path":["products",0,"price"]}]},{"id":"2","errors":[{"message":"Unexpected Execution Error","path":["products",0,"price"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1
```
