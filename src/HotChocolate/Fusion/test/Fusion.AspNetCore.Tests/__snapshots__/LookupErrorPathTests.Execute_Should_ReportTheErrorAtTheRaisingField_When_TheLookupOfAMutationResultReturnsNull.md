# Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAMutationResultReturnsNull

## mutation { createUser(id: 2) { boom nick title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["createUser","boom"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { nick boom title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["createUser","boom"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { nick title boom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["createUser","boom"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { soft nick title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["createUser","soft"]}],"data":{"createUser":{"soft":null,"nick":null,"title":null,"label":"label-2"}}}
```

## mutation { createUser(id: 2) { nick soft title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["createUser","soft"]}],"data":{"createUser":{"nick":null,"soft":null,"title":null,"label":"label-2"}}}
```

## mutation { createUser(id: 2) { nick title soft label } }

```text
{"errors":[{"message":"soft failed for 2","path":["createUser","soft"]}],"data":{"createUser":{"nick":null,"title":null,"soft":null,"label":"label-2"}}}
```

## mutation { createUser(id: 2) { nick soft code label } }

```text
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["createUser","code"],"extensions":{"code":"HC0018"}},{"message":"soft failed for 2","path":["createUser","soft"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { nick ghost label } }

```text
{"errors":[{"message":"hidden failed for 2"}],"data":{"createUser":{"nick":null,"ghost":null,"label":"label-2"}}}
```

## mutation { createUser(id: 2) { nick boom xboom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["createUser","boom"]},{"message":"xboom failed for 2","path":["createUser","xboom"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { nick soft xboom label } }

```text
{"errors":[{"message":"soft failed for 2","path":["createUser","soft"]},{"message":"xboom failed for 2","path":["createUser","xboom"]}],"data":{"createUser":null}}
```

## mutation { createUser(id: 2) { nick ghost xboom label } }

```text
{"errors":[{"message":"hidden failed for 2"},{"message":"xboom failed for 2","path":["createUser","xboom"]}],"data":{"createUser":null}}
```
