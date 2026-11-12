# Execute_Should_ReportTheErrorAtTheRaisingField_When_TheLookupOfAListItemReturnsNull

## { users { boom nick title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["users",1,"boom"]}],"data":{"users":[{"boom":"boom-1","nick":"nick-1","title":"title-1","label":"label-1"},null,{"boom":"boom-3","nick":"nick-3","title":"title-3","label":"label-3"}]}}
```

## { users { nick boom title label } }

```text
{"errors":[{"message":"boom failed for 2","path":["users",1,"boom"]}],"data":{"users":[{"nick":"nick-1","boom":"boom-1","title":"title-1","label":"label-1"},null,{"nick":"nick-3","boom":"boom-3","title":"title-3","label":"label-3"}]}}
```

## { users { nick title boom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["users",1,"boom"]}],"data":{"users":[{"nick":"nick-1","title":"title-1","boom":"boom-1","label":"label-1"},null,{"nick":"nick-3","title":"title-3","boom":"boom-3","label":"label-3"}]}}
```

## { users { soft nick title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["users",1,"soft"]}],"data":{"users":[{"soft":"soft-1","nick":"nick-1","title":"title-1","label":"label-1"},{"soft":null,"nick":null,"title":null,"label":"label-2"},{"soft":"soft-3","nick":"nick-3","title":"title-3","label":"label-3"}]}}
```

## { users { nick soft title label } }

```text
{"errors":[{"message":"soft failed for 2","path":["users",1,"soft"]}],"data":{"users":[{"nick":"nick-1","soft":"soft-1","title":"title-1","label":"label-1"},{"nick":null,"soft":null,"title":null,"label":"label-2"},{"nick":"nick-3","soft":"soft-3","title":"title-3","label":"label-3"}]}}
```

## { users { nick title soft label } }

```text
{"errors":[{"message":"soft failed for 2","path":["users",1,"soft"]}],"data":{"users":[{"nick":"nick-1","title":"title-1","soft":"soft-1","label":"label-1"},{"nick":null,"title":null,"soft":null,"label":"label-2"},{"nick":"nick-3","title":"title-3","soft":"soft-3","label":"label-3"}]}}
```

## { users { nick soft code label } }

```text
{"errors":[{"message":"Cannot return null for non-nullable field.","path":["users",1,"code"],"extensions":{"code":"HC0018"}},{"message":"soft failed for 2","path":["users",1,"soft"]}],"data":{"users":[{"nick":"nick-1","soft":"soft-1","code":"code-1","label":"label-1"},null,{"nick":"nick-3","soft":"soft-3","code":"code-3","label":"label-3"}]}}
```

## { users { nick ghost label } }

```text
{"errors":[{"message":"hidden failed for 2"}],"data":{"users":[{"nick":"nick-1","ghost":"ghost-1","label":"label-1"},{"nick":null,"ghost":null,"label":"label-2"},{"nick":"nick-3","ghost":"ghost-3","label":"label-3"}]}}
```

## { users { nick boom xboom label } }

```text
{"errors":[{"message":"boom failed for 2","path":["users",1,"boom"]},{"message":"xboom failed for 2","path":["users",1,"xboom"]}],"data":{"users":[{"nick":"nick-1","boom":"boom-1","xboom":"xboom-1","label":"label-1"},null,{"nick":"nick-3","boom":"boom-3","xboom":"xboom-3","label":"label-3"}]}}
```

## { users { nick soft xboom label } }

```text
{"errors":[{"message":"soft failed for 2","path":["users",1,"soft"]},{"message":"xboom failed for 2","path":["users",1,"xboom"]}],"data":{"users":[{"nick":"nick-1","soft":"soft-1","xboom":"xboom-1","label":"label-1"},null,{"nick":"nick-3","soft":"soft-3","xboom":"xboom-3","label":"label-3"}]}}
```

## { users { nick ghost xboom label } }

```text
{"errors":[{"message":"hidden failed for 2"},{"message":"xboom failed for 2","path":["users",1,"xboom"]}],"data":{"users":[{"nick":"nick-1","ghost":"ghost-1","xboom":"xboom-1","label":"label-1"},null,{"nick":"nick-3","ghost":"ghost-3","xboom":"xboom-3","label":"label-3"}]}}
```
