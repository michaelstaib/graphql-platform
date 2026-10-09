# ReadResultsAsync_Should_EndTheStreamWithAnErrorResultInsteadOfTheEvent_When_TheVerdictChangeScopeFailsToCommit

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
  "System.InvalidOperationException: commit"
]
```
