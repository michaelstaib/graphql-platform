using HotChocolate.Fusion.Types;
using HotChocolate.Language;

namespace HotChocolate.Fusion.Planning;

public class DeferLoweringTests : FusionTestBase
{
    private static readonly FusionSchemaDefinition s_schema = ComposeSchema(
        """
        # name: a
        type Query {
            user(id: ID!): User @lookup
        }

        type Mutation {
            updateUser(id: ID!): User
        }

        type User @key(fields: "id") {
            id: ID!
            name: String!
            sku: String!
            profile: Profile!
        }

        type Profile {
            bio: String!
            avatar: String!
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
    public void Lower_Should_InlineFragment_When_EveryFieldIsAlreadySelected()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_InlineEnclosingFragment_When_InlinedNestedFragmentLeavesNothingNew()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_KeepEnclosingFragmentDeferred_When_InlinedNestedFragmentLeavesOtherFieldsNew()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_DropOnlyOverlappingLeaves_When_CompositeFieldOverlaps()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_DropDeferDirective_When_IfArgumentIsFalse()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_NestSkipCopy_When_DeferredFragmentHasSkip()
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
                }
                ... @skip(if: $s) @defer(if: $d) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOperation_When_OperationHasNoDefer()
    {
        // arrange
        // act
        var lowered = Lower(
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
    public void Lower_Should_KeepBothFragmentsDeferred_When_FragmentHoldsOnlyNestedFragment()
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
                ... @defer(label: "outer") {
                  ... @defer(label: "inner") {
                    email
                  }
                }
              }
            }
            """);
    }

    private static string Lower(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var operation = document.Definitions.OfType<OperationDefinitionNode>().First();
        return DeferLowering.Lower(operation, s_schema).ToString(indented: true);
    }
}
