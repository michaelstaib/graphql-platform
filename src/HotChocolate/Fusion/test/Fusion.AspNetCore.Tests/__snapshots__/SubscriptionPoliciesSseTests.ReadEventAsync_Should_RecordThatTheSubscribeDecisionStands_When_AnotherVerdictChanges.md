# ReadEventAsync_Should_RecordThatTheSubscribeDecisionStands_When_AnotherVerdictChanges

## Event

```text
event: next
{
  "errors": [
    {
      "message": "The current user is not authorized to access this resource.",
      "path": [
        "changed",
        "name"
      ],
      "extensions": {
        "code": "AUTH_NOT_AUTHORIZED"
      }
    },
    {
      "message": "The current user is not authorized to access this resource.",
      "path": [
        "changed",
        "note"
      ],
      "extensions": {
        "code": "AUTH_NOT_AUTHORIZED"
      }
    }
  ],
  "data": {
    "changed": {
      "id": "1",
      "name": null,
      "note": null
    }
  }
}
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Denied revoked; Item.note @policy(flip) Allowed - | failure=-",
  "commits=1 | Item.name @policy(live) Denied subscribe-time decision stands; Item.note @policy(flip) Denied revoked | failure=-"
]
```
