# Execute_Should_ReportTheErrorOfEachSourceOnItsSubFieldInEveryOrder_When_ARootValueTypeFails

## A viewer: Viewer! @shareable @error { a: String }, B viewer: Viewer! @shareable { b: String }

```text
data: {"viewer":{"a":null,"b":"Viewer"}}
errors: [{"message":"Unexpected Execution Error","path":["viewer","a"]}]

```

## A viewer: Viewer! @shareable @error { a: String! }, B viewer: Viewer! @shareable { b: String }

```text
data: null
errors: [{"message":"Unexpected Execution Error","path":["viewer","a"]}]

```

## A viewer: Viewer! @shareable { a: String } fails, B viewer: Viewer! @shareable { b: String } fails

```text
data: {"viewer":{"a":null,"b":null}}
errors: [{"message":"Unexpected Execution Error","path":["viewer","a"]},{"message":"Unexpected Execution Error","path":["viewer","b"]}]

```

## A viewer: Viewer @shareable { a: String } fails, B viewer: Viewer @shareable { b: String } fails

```text
data: {"viewer":{"a":null,"b":null}}
errors: [{"message":"Unexpected Execution Error","path":["viewer","a"]},{"message":"Unexpected Execution Error","path":["viewer","b"]}]

```

## A viewer: Viewer! @shareable { a: String! } fails, B viewer: Viewer! @shareable { b: String } fails

```text
data: null
errors: [{"message":"Unexpected Execution Error","path":["viewer","a"]},{"message":"Unexpected Execution Error","path":["viewer","b"]}]

```
