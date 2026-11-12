# Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAnEntityReturnsNull

## { user(id: 2) { boom nick title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["user","boom"]}],"data":{"user":null}}
```

## { user(id: 2) { nick boom title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["user","boom"]}],"data":{"user":null}}
```

## { user(id: 2) { nick title boom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["user","boom"]}],"data":{"user":null}}
```

## { user(id: 2) { soft nick title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"user":{"soft":null,"nick":null,"title":null,"label":"label-2"}}}
```

## { user(id: 2) { nick soft title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"user":{"nick":null,"soft":null,"title":null,"label":"label-2"}}}
```

## { user(id: 2) { nick title soft label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"user":{"nick":null,"title":null,"soft":null,"label":"label-2"}}}
```

## { user(id: 2) { nick soft code label } }

```text
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["user","code"],"extensions":{"code":"HC0018"}},{"message":"soft failed for 2","path":["user","soft"]}],"data":{"user":null}}
```

## { user(id: 2) { nick ghost label } }

```text
{"errors":[{"message":"hidden failed for 2"}],"data":{"user":{"nick":null,"ghost":null,"label":"label-2"}}}
```

## { user(id: 2) { nick boom xboom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["user","boom"]},{"message":"xboom failed for 2","path":["user","xboom"]}],"data":{"user":null}}
```

## { user(id: 2) { nick soft xboom label } }

```text
{"errors":[{"message":"soft failed for 2","path":["user","soft"]},{"message":"xboom failed for 2","path":["user","xboom"]}],"data":{"user":null}}
```

## { user(id: 2) { nick ghost xboom label } }

```text
{"errors":[{"message":"hidden failed for 2"},{"message":"xboom failed for 2","path":["user","xboom"]}],"data":{"user":null}}
```
