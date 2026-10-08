# ReadResultsAsync_Should_EndTheStreamAndReportTheFailure_When_TheExpiryScopeFailsToCommit

## Failure

```text
System.InvalidOperationException: commit
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed - | failure=-",
  "commits=1 | Item.name @policy(live) Denied expired | failure=-"
]
```

## Request Errors

```json
[
  "System.InvalidOperationException: commit"
]
```

## Subscription Event Errors

```json
[]
```
