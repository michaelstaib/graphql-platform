# ReadResultsAsync_Should_EndTheStreamWithAnErrorResult_When_PolicyFaultsMidStream

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
