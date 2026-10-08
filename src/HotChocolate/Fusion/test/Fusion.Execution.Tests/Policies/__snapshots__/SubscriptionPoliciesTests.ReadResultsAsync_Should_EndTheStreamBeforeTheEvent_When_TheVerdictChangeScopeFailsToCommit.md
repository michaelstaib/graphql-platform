# ReadResultsAsync_Should_EndTheStreamBeforeTheEvent_When_TheVerdictChangeScopeFailsToCommit

## Failure

```text
System.InvalidOperationException: commit
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
