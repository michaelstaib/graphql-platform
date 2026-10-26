using System.Text.Json;

namespace StrawberryShake.Json;

public class JsonResultPatcherTests
{
    [Fact]
    public void PatchResponse_Should_MergeIncrementalData_When_PathTargetsAnObject()
    {
        // arrange
        var patcher = new JsonResultPatcher();
        patcher.SetResponse(CreateResponse("""{"data":{"hero":{"name":"R2-D2"}}}""", hasNext: true));
        var patch = CreateResponse(
            """{"incremental":[{"data":{"id":"1"},"path":["hero"],"label":"L"}]}""",
            isPatch: true);

        // act
        var patched = patcher.PatchResponse(patch);

        // assert
        patched.Body!.RootElement.ToString().MatchInlineSnapshot(
            """{"data":{"hero":{"name":"R2-D2","id":"1"}}}""");
    }

    [Fact]
    public void PatchResponse_Should_MergeIncrementalData_When_PathTargetsAListItem()
    {
        // arrange
        var patcher = new JsonResultPatcher();
        patcher.SetResponse(CreateResponse("""{"data":{"nodes":[{},{}]}}""", hasNext: true));
        var first = CreateResponse(
            """{"incremental":[{"data":{"name":"Han"},"path":["nodes",1]}]}""",
            isPatch: true,
            hasNext: true);
        var second = CreateResponse(
            """{"incremental":[{"data":{"name":"Luke"},"path":["nodes",0]}]}""",
            isPatch: true);

        // act
        patcher.PatchResponse(first);
        var patched = patcher.PatchResponse(second);

        // assert
        patched.Body!.RootElement.ToString().MatchInlineSnapshot(
            """{"data":{"nodes":[{"name":"Luke"},{"name":"Han"}]}}""");
    }

    [Fact]
    public void PatchResponse_Should_MergeData_When_PayloadCarriesTopLevelPath()
    {
        // arrange
        var patcher = new JsonResultPatcher();
        patcher.SetResponse(CreateResponse("""{"data":{"hero":{"name":"R2-D2"}}}""", hasNext: true));
        var patch = CreateResponse(
            """{"data":{"id":"1"},"path":["hero"]}""",
            isPatch: true);

        // act
        var patched = patcher.PatchResponse(patch);

        // assert
        patched.Body!.RootElement.ToString().MatchInlineSnapshot(
            """{"data":{"hero":{"name":"R2-D2","id":"1"}}}""");
    }

    [Fact]
    public void PatchResponse_Should_KeepData_When_IncrementalEntryHasNoData()
    {
        // arrange
        var patcher = new JsonResultPatcher();
        patcher.SetResponse(CreateResponse("""{"data":{"hero":{"name":"R2-D2"}}}""", hasNext: true));
        var patch = CreateResponse(
            """{"incremental":[{"errors":[{"message":"failed"}],"path":["hero"]}]}""",
            isPatch: true);

        // act
        var patched = patcher.PatchResponse(patch);

        // assert
        patched.Body!.RootElement.ToString().MatchInlineSnapshot(
            """{"data":{"hero":{"name":"R2-D2"}}}""");
    }

    private static Response<JsonDocument> CreateResponse(
        string json,
        bool isPatch = false,
        bool hasNext = false)
        => new(JsonDocument.Parse(json), null, isPatch, hasNext);
}
