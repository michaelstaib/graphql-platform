using System.Text.Json.Nodes;

namespace HotChocolate.Fusion;

internal static class DeferredPayloadAssert
{
    public static void NoErrors(JsonObject payload)
    {
        if (payload["errors"] is JsonArray errors)
        {
            Assert.Fail($"The payload contains errors: {errors.ToJsonString()}");
        }

        foreach (var incremental in payload["incremental"]?.AsArray() ?? [])
        {
            if (incremental!["errors"] is JsonArray incrementalErrors)
            {
                Assert.Fail(
                    $"The incremental entry '{incremental["id"]}' contains errors: {incrementalErrors.ToJsonString()}");
            }
        }

        foreach (var completed in payload["completed"]?.AsArray() ?? [])
        {
            if (completed!["errors"] is JsonArray completedErrors)
            {
                Assert.Fail(
                    $"The delivery group '{completed["id"]}' completed with errors: {completedErrors.ToJsonString()}");
            }
        }
    }
}
