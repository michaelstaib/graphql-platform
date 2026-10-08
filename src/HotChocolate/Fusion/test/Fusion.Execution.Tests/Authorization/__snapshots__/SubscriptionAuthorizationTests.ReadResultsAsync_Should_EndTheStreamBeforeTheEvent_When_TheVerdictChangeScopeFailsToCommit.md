# ReadResultsAsync_Should_EndTheStreamBeforeTheEvent_When_TheVerdictChangeScopeFailsToCommit

## Failure

```text
System.InvalidOperationException: commit
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed -",
  "commits=1 | Item.name @policy(live) Denied revoked"
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
[
  "System.InvalidOperationException: commit"
]
```
