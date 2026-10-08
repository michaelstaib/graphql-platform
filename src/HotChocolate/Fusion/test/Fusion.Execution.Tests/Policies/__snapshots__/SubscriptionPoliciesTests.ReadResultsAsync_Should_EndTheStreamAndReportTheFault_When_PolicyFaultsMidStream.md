# ReadResultsAsync_Should_EndTheStreamAndReportTheFault_When_PolicyFaultsMidStream

## Failure

```text
System.InvalidOperationException: boom
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed - | failure=-",
  "commits=1 | Item.name @policy(live) Unanswered - | failure=System.InvalidOperationException: boom"
]
```

## Request Errors

```json
[]
```

## Subscription Event Errors

```json
[
  "System.InvalidOperationException: boom"
]
```
