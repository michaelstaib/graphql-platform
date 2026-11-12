# Execute_Should_ReportTheErrorOnlyOnFieldsOfTheRuntimeType_When_AnInterfaceHopFails

## s: false

```text
data: {"nullablePetList":[null,{"name":600}]}
errors: [{"message":"DN hop failed for 94","path":["nullablePetList",0,"barkNN"]},{"message":"PN hop failed for 94"}]

```

## s: true

```text
data: {"nullablePetList":[null,{"name":600}]}
errors: [{"message":"DN hop failed for 94","path":["nullablePetList",0,"barkNN"]},{"message":"PN hop failed for 94","path":["nullablePetList",0,"name"]}]

```
