---
title: Migrate Hot Chocolate from 16.7 to 16.8
metaTitle: "Hot Chocolate 16.8 Migration Guide"
description: "Migration guide for Hot Chocolate v16.7 to v16.8: replace ITimeProvider and DefaultTimeProvider with the .NET TimeProvider."
---

Update every `HotChocolate.*` package in the application to version 16.8 before applying these changes.

# Breaking changes

Things that have been removed or had a change in behavior that may cause your code not to compile or lead to unexpected behavior at runtime if not addressed.

## ITimeProvider and DefaultTimeProvider removed

`HotChocolate.Execution.ITimeProvider` and `HotChocolate.Execution.DefaultTimeProvider` have been removed. Hot Chocolate and the Fusion gateway now use the .NET `System.TimeProvider` and register `TimeProvider.System` as the default in the schema services. A `TimeProvider` registered in the schema services replaces the default. In a Hot Chocolate server, it supplies the `Last-Modified` header of schema downloads and drives the request and subscription event timeouts. In the Fusion gateway, it supplies only the `Last-Modified` header of schema downloads and does not drive the gateway's request timeout.

The `timeProvider` parameter of the `DefaultHttpResponseFormatter` constructors is now of type `TimeProvider`.

Replace a custom `ITimeProvider` with a `TimeProvider` registration:

```diff
-public sealed class FixedTimeProvider : ITimeProvider
-{
-    public DateTimeOffset UtcNow { get; } = new(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);
-}
-
 builder.Services
     .AddGraphQLServer()
     .ConfigureSchemaServices(services =>
-        services
-            .RemoveAll<ITimeProvider>()
-            .AddSingleton<ITimeProvider, FixedTimeProvider>());
+        services.AddSingleton<TimeProvider>(
+            new FakeTimeProvider(new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero))));
```

`FakeTimeProvider` is part of the `Microsoft.Extensions.TimeProvider.Testing` package. Any other `TimeProvider` subclass can be registered the same way. Code that read `ITimeProvider.UtcNow` calls `TimeProvider.GetUtcNow()` instead.
