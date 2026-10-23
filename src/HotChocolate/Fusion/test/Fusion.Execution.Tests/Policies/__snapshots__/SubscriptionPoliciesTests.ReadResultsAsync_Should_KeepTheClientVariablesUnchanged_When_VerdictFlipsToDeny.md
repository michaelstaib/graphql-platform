# ReadResultsAsync_Should_KeepTheClientVariablesUnchanged_When_VerdictFlipsToDeny

## First event

```json
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

## Second event

```json
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
    }
  ],
  "data": {
    "changed": {
      "id": "1",
      "name": null,
      "tag": "t"
    }
  }
}
```

## Client variables

```json
[
  "tag"
]
```

## Subscribe variables

```json
[
  "tag",
  "__fusion_policy_1"
]
```
