# Execute_Should_ReportTheErrorAtTheRaisingField_When_TheDeferredLookupOfAnEntityReturnsNull

## { user(id: 2) { id ... @defer { boom nick title label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"boom failed for 2","path":["user","boom"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick boom title label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"boom failed for 2","path":["user","boom"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick title boom label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"boom failed for 2","path":["user","boom"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { soft nick title label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"incremental":[{"id":"0","errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"soft":null,"nick":null,"title":null,"label":"label-2"}}],"completed":[{"id":"0"}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick soft title label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"incremental":[{"id":"0","errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"nick":null,"soft":null,"title":null,"label":"label-2"}}],"completed":[{"id":"0"}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick title soft label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"incremental":[{"id":"0","errors":[{"message":"soft failed for 2","path":["user","soft"]}],"data":{"nick":null,"title":null,"soft":null,"label":"label-2"}}],"completed":[{"id":"0"}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick soft code label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"Cannot return null for non-nullable field.","path":["user","code"],"extensions":{"code":"HC0018"}},{"message":"soft failed for 2","path":["user","soft"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick ghost label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"incremental":[{"id":"0","errors":[{"message":"hidden failed for 2"}],"data":{"nick":null,"ghost":null,"label":"label-2"}}],"completed":[{"id":"0"}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick boom xboom label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"boom failed for 2","path":["user","boom"]},{"message":"xboom failed for 2","path":["user","xboom"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick soft xboom label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"soft failed for 2","path":["user","soft"]},{"message":"xboom failed for 2","path":["user","xboom"]}]}],"hasNext":false}

```

## { user(id: 2) { id ... @defer { nick ghost xboom label } } }

```text
{"data":{"user":{"id":2}},"pending":[{"id":"0","path":["user"]}],"hasNext":true}
{"completed":[{"id":"0","errors":[{"message":"hidden failed for 2"},{"message":"xboom failed for 2","path":["user","xboom"]}]}],"hasNext":false}

```
