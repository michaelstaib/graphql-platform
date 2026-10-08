# ReadResultsAsync_Should_EndTheStreamAndCommitAScope_When_TheTokenExpires

## Event

```text
{
  "data": {
    "changed": {
      "id": "1",
      "name": "n",
      "tag": "t"
    }
  }
}
```

## Has Next

```json
false
```

## Scopes

```json
[
  "commits=1 | Item.name @policy(live) Allowed -",
  "commits=1 | Item.name @policy(live) Denied expired"
]
```
