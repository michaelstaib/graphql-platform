# ReadResultsAsync_Should_RecordEveryOccurrence_When_AReevaluatedVerdictChangesBesideAFrozenPolicy

```json
[
  "commits=1 | Item.name @policy(live) Allowed -; Item.note @policy(flip) Allowed -",
  "commits=1 | Item.name @policy(live) Allowed frozen; Item.note @policy(flip) Denied revoked"
]
```
