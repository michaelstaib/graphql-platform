using HotChocolate.Language;

namespace HotChocolate.Fusion.Execution.Rewriters;

public sealed class ArgumentLiteralRewriterTests : FusionTestBase
{
    private const string Schema =
        """
        type Query {
          productById(id: ID!): Product
          products(first: Int, filter: ProductFilter, unit: Unit, tags: [String!]): [Product!]!
          media: [Media]
        }

        type Product {
          id: ID!
          name: String!
          weight(unit: Unit): Float
          reviews(first: Int): [String!]
        }

        input ProductFilter {
          name: String
          minPrice: Float
          range: RangeInput
        }

        input RangeInput {
          from: Int
          to: Int
        }

        enum Unit {
          KG
          LB
        }

        interface Media {
          id: ID!
        }

        type Book implements Media {
          id: ID!
          cover(size: Int): String
        }

        type Movie implements Media {
          id: ID!
          cover(size: Int!): String
        }
        """;

    [Fact]
    public void TryRewrite_Should_ReplaceLiteralsWithVariables_When_ArgumentsAreLiterals()
    {
        // arrange
        const string operation =
            """
            query GetProducts {
              productById(id: "1") {
                name
                weight(unit: KG)
              }
              products(first: 5, filter: { name: "a", range: { from: 1, to: 2 } }, unit: null, tags: ["x", "y"]) {
                id
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query GetProducts(
              $__fusion_literal_0: ID!
              $__fusion_literal_1: Unit
              $__fusion_literal_2: Int
              $__fusion_literal_3: ProductFilter
              $__fusion_literal_4: Unit
              $__fusion_literal_5: [String!]
            ) {
              productById(id: $__fusion_literal_0) {
                name
                weight(unit: $__fusion_literal_1)
              }
              products(
                first: $__fusion_literal_2
                filter: $__fusion_literal_3
                unit: $__fusion_literal_4
                tags: $__fusion_literal_5
              ) {
                id
              }
            }
            """);
        values.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "1" """,
            "__fusion_literal_1 = KG",
            "__fusion_literal_2 = 5",
            """__fusion_literal_3 = { name: "a", range: { from: 1, to: 2 } }""",
            "__fusion_literal_4 = null",
            """__fusion_literal_5 = ["x", "y"]"""
        ]);
    }

    [Fact]
    public void TryRewrite_Should_ProduceEqualOperations_When_OperationsDifferOnlyInLiterals()
    {
        // arrange
        const string first = """{ productById(id: "1") { reviews(first: 2) } }""";
        const string second = """{ productById(id: "42") { reviews(first: 10) } }""";

        // act
        TryRewrite(first, out var firstRewritten, out var firstValues);
        TryRewrite(second, out var secondRewritten, out var secondValues);

        // assert
        Assert.Equal(firstRewritten!.ToString(), secondRewritten!.ToString());
        firstValues.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "1" """,
            "__fusion_literal_1 = 2"
        ]);
        secondValues.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "42" """,
            "__fusion_literal_1 = 10"
        ]);
    }

    [Fact]
    public void TryRewrite_Should_KeepArgument_When_ValueContainsVariable()
    {
        // arrange
        const string operation =
            """
            query ($id: ID!, $from: Int) {
              productById(id: $id) {
                name
              }
              products(first: 5, filter: { name: "a", range: { from: $from, to: 2 } }, tags: ["x", $id]) {
                id
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query($id: ID!, $from: Int, $__fusion_literal_0: Int) {
              productById(id: $id) {
                name
              }
              products(
                first: $__fusion_literal_0
                filter: { name: "a", range: { from: $from, to: 2 } }
                tags: ["x", $id]
              ) {
                id
              }
            }
            """);
        values.MatchInlineSnapshots(["__fusion_literal_0 = 5"]);
    }

    [Fact]
    public void TryRewrite_Should_KeepDirectiveArguments_When_DirectivesHaveLiterals()
    {
        // arrange
        const string operation =
            """
            {
              productById(id: "1") @skip(if: false) {
                name @include(if: true)
                ... @defer(label: "weight", if: true) {
                  weight(unit: LB)
                }
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query($__fusion_literal_0: ID!, $__fusion_literal_1: Unit) {
              productById(id: $__fusion_literal_0) @skip(if: false) {
                name @include(if: true)
                ... @defer(label: "weight", if: true) {
                  weight(unit: $__fusion_literal_1)
                }
              }
            }
            """);
        values.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "1" """,
            "__fusion_literal_1 = LB"
        ]);
    }

    [Fact]
    public void TryRewrite_Should_KeepIntrospectionArguments_When_IntrospectionFieldsHaveLiterals()
    {
        // arrange
        const string operation =
            """
            {
              __type(name: "Product") {
                fields(includeDeprecated: true) {
                  name
                }
              }
              productById(id: "1") {
                __typename
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query($__fusion_literal_0: ID!) {
              __type(name: "Product") {
                fields(includeDeprecated: true) {
                  name
                }
              }
              productById(id: $__fusion_literal_0) {
                __typename
              }
            }
            """);
        values.MatchInlineSnapshots(["""__fusion_literal_0 = "1" """]);
    }

    [Fact]
    public void TryRewrite_Should_SkipVariableName_When_ClientVariableHasTheName()
    {
        // arrange
        const string operation =
            """
            query ($__fusion_literal_0: Int, $__fusion_literal_2: Unit) {
              products(first: $__fusion_literal_0, unit: $__fusion_literal_2, tags: ["x"]) {
                weight(unit: KG)
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query(
              $__fusion_literal_0: Int
              $__fusion_literal_2: Unit
              $__fusion_literal_1: [String!]
              $__fusion_literal_3: Unit
            ) {
              products(
                first: $__fusion_literal_0
                unit: $__fusion_literal_2
                tags: $__fusion_literal_1
              ) {
                weight(unit: $__fusion_literal_3)
              }
            }
            """);
        values.MatchInlineSnapshots(
        [
            """__fusion_literal_1 = ["x"]""",
            "__fusion_literal_3 = KG"
        ]);
    }

    [Fact]
    public void TryRewrite_Should_ReuseVariable_When_LiteralAndTypeAreEqual()
    {
        // arrange
        const string operation =
            """
            {
              a: productById(id: "1") {
                reviews(first: 3)
              }
              b: productById(id: "1") {
                reviews(first: 3)
              }
              products(first: 3) {
                id
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query($__fusion_literal_0: ID!, $__fusion_literal_1: Int) {
              a: productById(id: $__fusion_literal_0) {
                reviews(first: $__fusion_literal_1)
              }
              b: productById(id: $__fusion_literal_0) {
                reviews(first: $__fusion_literal_1)
              }
              products(first: $__fusion_literal_1) {
                id
              }
            }
            """);
        values.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "1" """,
            "__fusion_literal_1 = 3"
        ]);
    }

    [Fact]
    public void TryRewrite_Should_KeepLiterals_When_FieldsWithSameResponseNameNeedDifferentTypes()
    {
        // arrange
        const string operation =
            """
            {
              productById(id: "1") {
                name
              }
              media {
                ... on Book {
                  cover(size: 1)
                  small: cover(size: 2)
                }
                ... on Movie {
                  cover(size: 1)
                  large: cover(size: 2)
                }
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.True(success);
        rewritten!.MatchInlineSnapshot(
            """
            query(
              $__fusion_literal_0: ID!
              $__fusion_literal_1: Int
              $__fusion_literal_2: Int!
            ) {
              productById(id: $__fusion_literal_0) {
                name
              }
              media {
                ... on Book {
                  cover(size: 1)
                  small: cover(size: $__fusion_literal_1)
                }
                ... on Movie {
                  cover(size: 1)
                  large: cover(size: $__fusion_literal_2)
                }
              }
            }
            """);
        values.MatchInlineSnapshots(
        [
            """__fusion_literal_0 = "1" """,
            "__fusion_literal_1 = 2",
            "__fusion_literal_2 = 2"
        ]);
    }

    [Fact]
    public void TryRewrite_Should_ReturnFalse_When_OperationHasNoArgumentLiterals()
    {
        // arrange
        const string operation =
            """
            query ($id: ID!) {
              productById(id: $id) {
                name
              }
              __type(name: "Product") {
                name
              }
            }
            """;

        // act
        var success = TryRewrite(operation, out var rewritten, out var values);

        // assert
        Assert.False(success);
        Assert.Null(rewritten);
        Assert.Empty(values);
    }

    private static bool TryRewrite(
        string sourceText,
        out OperationDefinitionNode? rewritten,
        out string[] values)
    {
        var schema = ComposeSchema(Schema);
        var operation = Utf8GraphQLParser.Parse(sourceText).Definitions.OfType<OperationDefinitionNode>().Single();
        var rewriter = new ArgumentLiteralRewriter(schema);

        var success = rewriter.TryRewrite(operation, out rewritten, out var variableValues);
        values = [.. variableValues.Select(value => $"{value.Name} = {value.Value}")];
        return success;
    }
}
