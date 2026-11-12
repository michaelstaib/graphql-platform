# Execute_Should_ReportTheErrorOnTheDependentOrOnceWithoutPath_When_TheLookupFailsBelowAValueType

## query($s: Boolean! = false) { user(id: 7) { info { region } total @include(if: $s) } }

```text
{"errors":[{"message":"sku failed for 7"}],"data":{"user":{"info":{"region":"eu"}}}}
```

## { user(id: 7) { info { region } total } }

```text
{"errors":[{"message":"sku failed for 7","path":["user","total"]}],"data":{"user":{"info":{"region":"eu"},"total":null}}}
```

## query($s: Boolean! = false) { user(id: 8) { info { region } total @include(if: $s) } }

```text
{"errors":[{"message":"info failed for 8"}],"data":{"user":{"info":{"region":"eu"}}}}
```

## { user(id: 8) { info { region } total } }

```text
{"errors":[{"message":"info failed for 8","path":["user","total"]}],"data":{"user":{"info":{"region":"eu"},"total":null}}}
```

## query($s: Boolean! = false) { user(id: 9) { info { region } total @include(if: $s) } }

```text
{"errors":[{"message":"entity failed for 9"}],"data":{"user":{"info":{"region":"eu"}}}}
```

## query($s: Boolean! = false) { user(id: 7) { info { region } xboom total @include(if: $s) } }

```text
{"errors":[{"message":"sku failed for 7"},{"message":"xboom failed for 7","path":["user","xboom"]}],"data":{"user":null}}
```

## query($s: Boolean! = false) { user(id: 8) { info { region } xboom total @include(if: $s) } }

```text
{"errors":[{"message":"info failed for 8"},{"message":"xboom failed for 8","path":["user","xboom"]}],"data":{"user":null}}
```
