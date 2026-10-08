# ReadResultsAsync_Should_EndTheStreamAndReportTheFailure_When_TheExpiryScopeFailsToCommit

## Failure

```text
System.InvalidOperationException: commit
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed -",
  "commits=1 | Item.name @policy(live) Denied expired"
]
```

## Request Errors

```json
[
  "System.InvalidOperationException: commit"
]
```
