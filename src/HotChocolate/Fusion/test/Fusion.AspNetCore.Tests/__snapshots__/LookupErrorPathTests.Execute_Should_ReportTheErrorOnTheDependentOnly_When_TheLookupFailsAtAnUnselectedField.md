# Execute_Should_ReportTheErrorOnTheDependentOnly_When_TheLookupFailsAtAnUnselectedField

## { user(id: 5) { nick total label } }

```text
{"errors":[{"message":"hidden failed for 5","path":["user","total"]}],"data":{"user":{"nick":null,"total":null,"label":"label-5"}}}
```

## { user(id: 5) { total label } }

```text
{"errors":[{"message":"hidden failed for 5","path":["user","total"]}],"data":{"user":{"total":null,"label":"label-5"}}}
```

## { user(id: 5) { nick label } }

```text
{"errors":[{"message":"hidden failed for 5"}],"data":{"user":{"nick":null,"label":"label-5"}}}
```
