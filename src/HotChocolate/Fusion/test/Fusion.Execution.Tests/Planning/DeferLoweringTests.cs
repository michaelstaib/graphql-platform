using HotChocolate.Fusion.Execution.Rewriters;
using HotChocolate.Fusion.Types;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

public class DeferLoweringTests : FusionTestBase
{
    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        options =>
        {
            options.Merger.EnableGlobalObjectIdentification = true;
            options.Satisfiability.IgnoredNonAccessibleFields["Order.sku"] = ["C:Query.orderById<Order>"];
        },
        """
        # name: a
        type Query {
            user(id: ID!): User @lookup
            order(id: ID!): Order
        }

        type Mutation {
            updateUser(id: ID!): User
        }

        type User @key(fields: "id") {
            id: ID!
            name: String!
            sku: String!
            profile: Profile!
            friends: [User!]!
        }

        type Profile {
            bio: String!
            avatar: String!
        }

        type Order @key(fields: "id") {
            id: ID!
            sku: String!
        }
        """,
        """
        # name: b
        type Query {
            userById(id: ID!): User @lookup
        }

        type User @key(fields: "id") {
            id: ID!
            email: String!
            total: Int!
            nick: String!
        }
        """,
        """
        # name: c
        type Query {
            orderById(id: ID!): Order @lookup
        }

        type Order @key(fields: "id") {
            id: ID!
            total: Int!
        }
        """);

    [Fact]
    public void Lower_Should_KeepDeferWithoutCopy_When_DeferIsUnconditional()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "mail") {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... @defer(label: "mail") {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AddSkipCopyInFront_When_DeferIsSwitchable()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($d: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(if: $d, label: "mail") {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($d: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $d) {
                  email
                }
                ... @defer(if: $d, label: "mail") {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_CopyInnerFieldFourTimes_When_SwitchableDeferIsNested()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($a: Boolean!, $b: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(if: $a, label: "outer") {
                        name
                        ... @defer(if: $b, label: "inner") {
                            email
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($a: Boolean!, $b: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $a) {
                  name
                  ... @skip(if: $b) {
                    email
                  }
                  ... @defer(if: $b, label: "inner") {
                    email
                  }
                }
                ... @defer(if: $a, label: "outer") {
                  name
                  ... @skip(if: $b) {
                    email
                  }
                  ... @defer(if: $b, label: "inner") {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_FragmentIsAtMutationRoot()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            mutation {
                ... @defer {
                    updateUser(id: "1") {
                        id
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            mutation {
              ... {
                updateUser(id: "1") {
                  id
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_AnchorTypeHasNoLookup()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    profile {
                        bio
                        ... @defer {
                            avatar
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                profile {
                  bio
                  ... {
                    avatar
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_InlinedInnerFragmentLeavesNothingNew_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    sku
                    ... @defer(label: "outer") {
                        ... @defer(label: "inner") {
                            sku
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                sku
                ... {
                  ... {
                    sku
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOuterFragmentDeferred_When_InlinedInnerLeavesOtherFieldsNew_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    sku
                    ... @defer(label: "outer") {
                        total
                        ... @defer(label: "inner") {
                            sku
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                sku
                ... @defer(label: "outer") {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafFromDeferredCopy_When_LeafIsSelectedOutsideDeferredFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    id
                    sku
                    ... @defer {
                        sku
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                sku
                ... @defer {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepLeafInDeferredCopy_When_PlainSelectionIsConditional()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($x: Boolean!) {
                user(id: "1") {
                    sku @include(if: $x)
                    ... @defer {
                        sku
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($x: Boolean!) {
              user(id: "1") {
                sku @include(if: $x)
                ... @defer {
                  sku
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_NotRequireLookup_When_SwitchableDeferIsAtQueryRoot()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($d: Boolean!) {
                ... @defer(if: $d) {
                    user(id: "1") {
                        id
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($d: Boolean!) {
              ... @skip(if: $d) {
                user(id: "1") {
                  id
                }
              }
              ... @defer(if: $d) {
                user(id: "1") {
                  id
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropDeferDirective_When_IfArgumentIsFalse_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(if: false) {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepCopiesInEnclosingFragment_When_NormalizerMovedSkipOffTheDeferredFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($s: Boolean!, $d: Boolean!) {
                user(id: "1") {
                    id
                    ... @skip(if: $s) @defer(if: $d) {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($s: Boolean!, $d: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $s) {
                  ... @skip(if: $d) {
                    email
                  }
                  ... @defer(if: $d) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_NestSkipCopy_When_DeferredFragmentHasSkip_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query($s: Boolean!, $d: Boolean!) {
                user(id: "1") {
                    id
                    ... @skip(if: $s) @defer(if: $d) {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($s: Boolean!, $d: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $s) {
                  ... @skip(if: $d) {
                    email
                  }
                }
                ... @skip(if: $s) @defer(if: $d) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOperation_When_OperationHasNoDefer_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    ... @include(if: true) {
                        email
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... @include(if: true) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_EveryFieldIsAlreadySelected_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer {
                        id
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... {
                  id
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafFromDeferredCopy_When_LeafIsSelectedOutside_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    sku
                    ... @defer {
                        sku
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                sku
                ... @defer {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropOnlyOverlappingLeaves_When_CompositeFieldOverlaps_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    profile {
                        bio
                    }
                    ... @defer {
                        profile {
                            bio
                            avatar
                        }
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                profile {
                  bio
                }
                ... @defer {
                  profile {
                    avatar
                  }
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepLeafInDeferredCopy_When_PlainSelectionIsConditional_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query($x: Boolean!) {
                user(id: "1") {
                    sku @include(if: $x)
                    ... @defer {
                        sku
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($x: Boolean!) {
              user(id: "1") {
                sku @include(if: $x)
                ... @defer {
                  sku
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferred_When_FieldWithoutLookupIsSelectedOutside_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                order(id: "1") {
                    id
                    sku
                    ... @defer {
                        sku
                        total
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              order(id: "1") {
                id
                sku
                ... @defer {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_OuterHoldsOnlyNestedFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "outer") {
                        ... @defer(label: "inner") {
                            email
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... {
                  ... @defer(label: "inner") {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_OuterHoldsOnlyUnconditionalNestedFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($a: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(if: $a, label: "outer") {
                        ... @defer(label: "inner") {
                            email
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($a: Boolean!) {
              user(id: "1") {
                id
                ... {
                  ... @defer(label: "inner") {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_OuterHoldsOnlyCoveredFieldAndNestedFragment_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "outer") {
                        id
                        ... @defer(label: "inner") {
                            name
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... {
                  id
                  ... @defer(label: "inner") {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOuterFragmentDeferred_When_OuterHoldsOnlySwitchableNestedFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query($c: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(label: "outer") {
                        ... @defer(if: $c, label: "inner") {
                            email
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($c: Boolean!) {
              user(id: "1") {
                id
                ... @defer(label: "outer") {
                  ... @skip(if: $c) {
                    email
                  }
                  ... @defer(if: $c, label: "inner") {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOuterFragmentDeferred_When_OuterHoldsCompositeFieldWithNestedFragment()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    ... @defer(label: "outer") {
                        friends {
                            ... @defer(label: "inner") {
                                name
                            }
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                ... @defer(label: "outer") {
                  friends {
                    ... @defer(label: "inner") {
                      name
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_CoveredCompositeFieldHoldsOnlyNestedFragment_InputIsNotNormalized()
    {
        // arrange
        // act
        var lowered = LowerNotNormalized(
            """
            query {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(label: "outer") {
                        friends {
                            ... @defer(label: "inner") {
                                name
                            }
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                friends {
                  id
                }
                ... {
                  friends {
                    ... @defer(label: "inner") {
                      name
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferred_When_OnlyTypenameIsSelectedAndAnchorTypeHasLookup()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "type") {
                        __typename
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                id
                ... @defer(label: "type") {
                  __typename
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OnlyTypenameIsSelectedAndAnchorTypeHasNoLookup()
    {
        // arrange
        // act
        var lowered = Lower(
            """
            query {
                user(id: "1") {
                    profile {
                        bio
                        ... @defer(label: "type") {
                            __typename
                        }
                    }
                }
            }
            """);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                profile {
                  bio
                  ... {
                    __typename
                  }
                }
              }
            }
            """);
    }

    private static string Lower(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var normalized = new DocumentRewriter(s_schema).RewriteDocument(document, operationName: null);
        var operation = normalized.Definitions.OfType<OperationDefinitionNode>().First();
        return DeferLowering.Lower(operation, s_schema).ToString(indented: true);
    }

    private static string LowerNotNormalized(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var operation = document.Definitions.OfType<OperationDefinitionNode>().First();
        return DeferLowering.Lower(operation, s_schema).ToString(indented: true);
    }
}
