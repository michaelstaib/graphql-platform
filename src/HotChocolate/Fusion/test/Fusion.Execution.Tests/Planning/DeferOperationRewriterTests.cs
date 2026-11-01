using HotChocolate.Fusion.Execution.Nodes;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

public class DeferOperationRewriterTests
{
    [Fact]
    public void Split_Should_KeepInclude_When_TypeConditionedFragmentSelectsTypename()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                items {
                    id
                    ... @defer {
                        ... on Product @include(if: $x) {
                            __typename
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  items {
                    ... on Product @include(if: $x) {
                      __typename
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepInclude_When_FragmentEnclosingLeafGroupSelectsAliasedTypename()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                items {
                    id
                    ... @include(if: $x) {
                        ... @defer {
                            kind: __typename
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  items {
                    ... @include(if: $x) {
                      kind: __typename
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepSkip_When_FragmentEnclosingGroupSelectsCompositeField()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                products {
                    id
                    ... @skip(if: $x) {
                        ... @defer {
                            reviews {
                                id
                            }
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  products {
                    ... @skip(if: $x) {
                      reviews {
                        id
                      }
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepConditionsInOrder_When_FragmentsAreNested()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!, $y: Boolean!) {
                products {
                    id
                    ... @include(if: $x) {
                        ... on Product @skip(if: $y) {
                            ... @defer {
                                rating
                            }
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!, $y: Boolean!) {
                  products {
                    ... @include(if: $x) {
                      ... on Product @skip(if: $y) {
                        rating
                      }
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepInclude_When_DeferredGroupEnclosesConditionalDefer()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                products {
                    id
                    ... @defer(label: "outer") {
                        reviews {
                            id
                        }
                        ... @include(if: $x) {
                            ... @defer(label: "inner") {
                                rating
                            }
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            outer => outer.MatchInlineSnapshot(
                """
                {
                  products {
                    reviews {
                      id
                    }
                  }
                }
                """),
            inner => inner.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  products {
                    ... @include(if: $x) {
                      rating
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepBothBranches_When_SiblingFragmentsSelectSameCompositeFieldUnderDifferentConditions()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!, $y: Boolean!) {
                products {
                    id
                    ... @defer {
                        ... @include(if: $x) {
                            reviews {
                                id
                            }
                        }
                        ... @skip(if: $y) {
                            reviews {
                                body
                            }
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!, $y: Boolean!) {
                  products {
                    ... @include(if: $x) {
                      reviews {
                        id
                      }
                    }
                    ... @skip(if: $y) {
                      reviews {
                        body
                      }
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepInclude_When_EnclosingFieldHasInclude()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                products @include(if: $x) {
                    id
                    ... @defer {
                        reviews {
                            id
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  products @include(if: $x) {
                    reviews {
                      id
                    }
                  }
                }
                """));
    }

    [Fact]
    public void Split_Should_KeepSkip_When_EnclosingFieldHasSkip()
    {
        // arrange
        const string query =
            """
            query ($x: Boolean!) {
                products @skip(if: $x) {
                    id
                    ... @defer {
                        reviews {
                            id
                        }
                    }
                }
            }
            """;

        // act
        var operations = Split(query);

        // assert
        Assert.Collection(
            operations,
            operation => operation.MatchInlineSnapshot(
                """
                query($x: Boolean!) {
                  products @skip(if: $x) {
                    reviews {
                      id
                    }
                  }
                }
                """));
    }

    private static List<string> Split(string query)
    {
        var operation = (OperationDefinitionNode)Utf8GraphQLParser.Parse(query).Definitions[0];
        var partitioning = DeferPartitioner.Partition(operation, new DeferConditionCollection(1024));

        return new DeferOperationRewriter()
            .Split(operation, partitioning)
            .IncrementalPlanDescriptors
            .Select(descriptor => descriptor.Operation.ToString())
            .ToList();
    }
}
