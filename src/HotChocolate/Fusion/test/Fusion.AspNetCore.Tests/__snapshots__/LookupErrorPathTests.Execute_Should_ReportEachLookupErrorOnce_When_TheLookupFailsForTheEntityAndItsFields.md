# Execute_Should_ReportEachLookupErrorOnce_When_TheLookupFailsForTheEntityAndItsFields

## { user(id: 4) { soft nick label } }

```text
{"errors":[{"message":"boom failed for 4"},{"message":"entity failed for 4","path":["user","nick"]},{"message":"entity failed for 4","path":["user","soft"]},{"message":"soft failed for 4","path":["user","soft"]}],"data":{"user":{"soft":null,"nick":null,"label":"label-4"}}}
```

## { user(id: 4) { boom nick label } }

```text
{"errors":[{"message":"soft failed for 4"},{"message":"entity failed for 4","path":["user","boom"]},{"message":"boom failed for 4","path":["user","boom"]}],"data":{"user":null}}
```

## { user(id: 4) { nick soft code label } }

```text
{"errors":[{"message":"boom failed for 4"},{"message":"entity failed for 4","path":["user","code"]},{"message":"entity failed for 4","path":["user","nick"]},{"message":"entity failed for 4","path":["user","soft"]},{"message":"soft failed for 4","path":["user","soft"]}],"data":{"user":null}}
```

## { user(id: 4) { soft nick xboom label } }

```text
{"errors":[{"message":"boom failed for 4"},{"message":"entity failed for 4","path":["user","nick"]},{"message":"entity failed for 4","path":["user","soft"]},{"message":"soft failed for 4","path":["user","soft"]},{"message":"xboom failed for 4","path":["user","xboom"]}],"data":{"user":null}}
```

## { user(id: 4) { nick soft code xboom label } }

```text
{"errors":[{"message":"boom failed for 4"},{"message":"entity failed for 4","path":["user","code"]},{"message":"entity failed for 4","path":["user","nick"]},{"message":"entity failed for 4","path":["user","soft"]},{"message":"soft failed for 4","path":["user","soft"]},{"message":"xboom failed for 4","path":["user","xboom"]}],"data":{"user":null}}
```
