# ReadResultsAsync_Should_EndTheStreamAndReportTheFault_When_PolicyFaultsMidStream

## Failure

```text
System.InvalidOperationException: boom
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed -",
  "commits=1 | Item.name @policy(live) Unanswered -"
]
```

## Request Errors

```json
[
  "System.InvalidOperationException: boom"
]
```
