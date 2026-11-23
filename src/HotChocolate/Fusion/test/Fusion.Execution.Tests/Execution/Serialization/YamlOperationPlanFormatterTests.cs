using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Fusion.Execution.Nodes.Serialization;
using HotChocolate.Fusion.Policies;
using HotChocolate.Fusion.Text.Json;
using Microsoft.Extensions.ObjectPool;

namespace HotChocolate.Fusion.Execution.Serialization;

public class YamlOperationPlanFormatterTests : FusionTestBase
{
    [Fact]
    public void Format_Should_IncludeVariableSetsAndTransport_When_NodeHasTrace()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: products
            type Query { product(id: ID!): String }
            """);
        var plan = PlanOperation(schema, "query Product($id: ID!) { product(id: $id) }");
        var node = Assert.Single(plan.AllNodes);
        var variables = JsonSegment.Create(new ReadOnlySequence<byte>("""{"id":"123"}"""u8.ToArray()));
        var nodeTrace = new ExecutionNodeTrace
        {
            Id = node.Id,
            Status = ExecutionStatus.Success,
            Duration = TimeSpan.FromMilliseconds(1.5),
            VariableSets = [new VariableValues(CompactPath.Root, variables)],
            Transport = new ExecutionNodeTransportTrace
            {
                Uri = new Uri("https://products.example/graphql"),
                ContentType = "application/graphql-response+json"
            }
        };
        var trace = new OperationPlanTrace
        {
            Duration = nodeTrace.Duration,
            Nodes = ImmutableDictionary<int, ExecutionNodeTrace>.Empty.Add(node.Id, nodeTrace)
        };

        // act
        var yaml = new YamlOperationPlanFormatter().Format(plan, trace);

        // assert
        yaml.MatchSnapshot(extension: ".yaml");
    }

    [Fact]
    public void Format_Should_WriteRequirementConditions_When_RequirementHasConditions()
    {
        // arrange
        var schema = ComposeSchema(
            """
            # name: users
            type Query { user: User }
            type User @key(fields: "id") { id: ID! }
            """,
            """
            # name: emails
            type Query { userById(id: ID!): User @lookup @internal }
            type User @key(fields: "id") { id: ID! email: String }
            """);
        var originalPlan = PlanOperation(schema, "{ user { email } }");
        var json = JsonNode.Parse(new JsonOperationPlanFormatter().Format(originalPlan))!;
        var requirement = json["nodes"]!
            .AsArray()
            .Select(t => t!["requirements"])
            .First(t => t is not null)![0]!;
        requirement["conditions"] = new JsonArray(
            new JsonObject { ["variable"] = "$a", ["passingValue"] = false });
        var parser = new JsonOperationPlanParser(
            new OperationCompiler(schema, new DefaultObjectPoolProvider().CreateFieldMapPool()),
            TestPolicyResolver.Create());
        var plan = parser.Parse(Encoding.UTF8.GetBytes(json.ToJsonString()));

        // act
        var yaml = new YamlOperationPlanFormatter().Format(plan);

        // assert
        yaml.MatchSnapshot(extension: ".yaml");
    }
}
