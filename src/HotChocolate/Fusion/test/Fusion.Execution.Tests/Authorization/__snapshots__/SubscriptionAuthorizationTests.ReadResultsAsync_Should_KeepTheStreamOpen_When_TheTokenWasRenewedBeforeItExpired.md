# ReadResultsAsync_Should_KeepTheStreamOpen_When_TheTokenWasRenewedBeforeItExpired

## Event before the first expiry

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

## Event after the first expiry

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

## Has Next after the renewed expiry

```json
false
```
