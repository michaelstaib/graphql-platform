using HotChocolate.ApolloFederation.Resolvers;
using HotChocolate.ApolloFederation.Types;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.ApolloFederation;

public class EntitiesResolverErrorPathTests
{
    [Fact]
    public async Task ResolveAsync_Should_ReportTheErrorAtTheEntityIndex_When_ReferenceResolverThrowsGraphQLException()
    {
        // arrange
        var executor = await new ServiceCollection()
            .AddGraphQL()
            .AddApolloFederation()
            .AddType<ThrowingEntity>()
            .AddQueryType()
            .BuildRequestExecutorAsync(cancellationToken: TestContext.Current.CancellationToken);

        // act
        var result = await executor.ExecuteAsync(
            """
            {
              _entities(representations: [
                { id: "1", __typename: "ThrowingEntity" },
                { id: "2", __typename: "ThrowingEntity" },
                { id: "3", __typename: "ThrowingEntity" }
              ]) {
                ... on ThrowingEntity { id }
              }
            }
            """,
            TestContext.Current.CancellationToken);

        // assert
        result.MatchInlineSnapshot(
            """
            {
              "errors": [
                {
                  "message": "entity 2 failed",
                  "path": [
                    "_entities",
                    1
                  ]
                }
              ],
              "data": {
                "_entities": [
                  {
                    "id": "1"
                  },
                  null,
                  {
                    "id": "3"
                  }
                ]
              }
            }
            """);
    }

    public sealed class ThrowingEntity
    {
        [Key]
        public string Id { get; set; } = null!;

        [ReferenceResolver]
        public static ThrowingEntity? Get(string id)
            => id == "2"
                ? throw new GraphQLException("entity 2 failed")
                : new ThrowingEntity { Id = id };
    }
}
