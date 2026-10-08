# ReadResultsAsync_Should_EndTheStreamWithAFault_When_TheVerdictChangeCommitThrowsCancellation

## Failure

```text
System.InvalidOperationException: The authorization of the request faulted without the request being canceled.
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
