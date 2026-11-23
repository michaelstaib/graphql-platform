# Defer_Should_ReportTheLengthErrorForEachFragment_When_ASharedListFailsAfterAStepOfAnotherFailed

## Payload 1

```json
{"data":{"product":{"name":"Product: UHJvZHVjdDo3NzM3NjQwMDE="}},"pending":[{"id":"0","path":["product"],"label":"a"},{"id":"1","path":["product"],"label":"b"}],"hasNext":true}
```

## Payload 2

```json
{"completed":[{"id":"0","errors":[{"message":"Cannot merge shared list results with different lengths.","path":["product","package","parts"]}]}],"hasNext":true}
```

## Payload 3

```json
{"completed":[{"id":"1","errors":[{"message":"Cannot merge shared list results with different lengths.","path":["product","package","parts"]}]}],"hasNext":false}
```

## Requests

```text
A=1, B=1, C=1, E=2
```
