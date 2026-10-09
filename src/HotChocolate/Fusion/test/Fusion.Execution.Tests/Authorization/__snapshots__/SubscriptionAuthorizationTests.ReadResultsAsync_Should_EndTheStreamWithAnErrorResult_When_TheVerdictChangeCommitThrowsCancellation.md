# ReadResultsAsync_Should_EndTheStreamWithAnErrorResult_When_TheVerdictChangeCommitThrowsCancellation

## Terminal Result

```text
{
  "errors": [
    {
      "message": "Unexpected Execution Error"
    }
  ]
}
```

## Terminal Status Code

```json
"InternalServerError"
```

## Has Next

```json
false
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed - | failure=-",
  "commits=1 | Item.name @policy(live) Denied revoked | failure=-"
]
```

## Request Errors

```json
[]
```

## Subscription Event Errors

```json
[
  "System.InvalidOperationException: The authorization of the request faulted without the request being canceled."
]
```
