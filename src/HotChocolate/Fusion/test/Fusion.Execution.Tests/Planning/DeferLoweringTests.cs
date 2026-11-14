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
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "mail") {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

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
    public void Lower_Should_AddSkipCopyAndIncludeDeferredCopy_When_DeferIsSwitchable()
    {
        // arrange
        const string operation =
            """
            query($d: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(if: $d, label: "mail") {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($d: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $d) {
                  email
                }
                ... @include(if: $d) @defer(if: $d, label: "mail") {
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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
                  ... @include(if: $b) @defer(if: $b, label: "inner") {
                    email
                  }
                }
                ... @include(if: $a) @defer(if: $a, label: "outer") {
                  name
                  ... @skip(if: $b) {
                    email
                  }
                  ... @include(if: $b) @defer(if: $b, label: "inner") {
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
        const string operation =
            """
            mutation {
                ... @defer {
                    updateUser(id: "1") {
                        id
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
    public void Lower_Should_KeepRemainingLeafInDeferredCopy_When_NormalizerAlreadyDroppedSelectedLeaf()
    {
        // arrange
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
            """
            query($d: Boolean!) {
                ... @defer(if: $d) {
                    user(id: "1") {
                        id
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($d: Boolean!) {
              ... @skip(if: $d) {
                user(id: "1") {
                  id
                }
              }
              ... @include(if: $d) @defer(if: $d) {
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
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(if: false) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
            """
            query($s: Boolean!, $d: Boolean!) {
                user(id: "1") {
                    id
                    ... @skip(if: $s) @defer(if: $d) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

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
                  ... @include(if: $d) @defer(if: $d) {
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
        const string operation =
            """
            query($s: Boolean!, $d: Boolean!) {
                user(id: "1") {
                    id
                    ... @skip(if: $s) @defer(if: $d) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
                ... @skip(if: $s) @include(if: $d) @defer(if: $d) {
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
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @include(if: true) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @defer {
                        id
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
                  ... @include(if: $c) @defer(if: $c, label: "inner") {
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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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
    public void Lower_Should_InlineOuterFragment_When_CoveredFieldHoldsOnlyNestedFragment_InputIsNotNormalized()
    {
        // arrange
        const string operation =
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
            """;

        // act
        var lowered = LowerNotNormalized(operation);

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
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "type") {
                        __typename
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

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
        const string operation =
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
            """;

        // act
        var lowered = Lower(operation);

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

    [Fact]
    public void Lower_Should_ExcludeNestedCopies_When_BothLevelsAreSwitchedByOneVariable()
    {
        // arrange
        const string operation =
            """
            query($v: Boolean!) {
                user(id: "1") {
                    id
                    ... @defer(if: $v, label: "outer") {
                        email
                        ... @defer(if: $v, label: "inner") {
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($v: Boolean!) {
              user(id: "1") {
                id
                ... @skip(if: $v) {
                  email
                  ... @skip(if: $v) {
                    nick
                  }
                  ... @include(if: $v) @defer(if: $v, label: "inner") {
                    nick
                  }
                }
                ... @include(if: $v) @defer(if: $v, label: "outer") {
                  email
                  ... @skip(if: $v) {
                    nick
                  }
                  ... @include(if: $v) @defer(if: $v, label: "inner") {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_ExcludeCopiesInsideEnclosingInclude_When_NormalizerMovedIncludeOffTheDeferredFragment()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!, $v: Boolean!) {
                user(id: "1") {
                    id
                    ... @include(if: $x) @defer(if: $v) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = Lower(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($x: Boolean!, $v: Boolean!) {
              user(id: "1") {
                id
                ... @include(if: $x) {
                  ... @skip(if: $v) {
                    email
                  }
                  ... @include(if: $v) @defer(if: $v) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_MoveIncludeToEnclosingFragment_When_IncludeIsOnOtherVariable_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!, $v: Boolean!) {
                user(id: "1") {
                    id
                    ... @include(if: $x) @defer(if: $v) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($x: Boolean!, $v: Boolean!) {
              user(id: "1") {
                id
                ... @include(if: $x) @skip(if: $v) {
                  email
                }
                ... @include(if: $x) {
                  ... @include(if: $v) @defer(if: $v) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AddNothing_When_DeferredFragmentHasIncludeOnSameVariable_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($v: Boolean!) {
                user(id: "1") {
                    id
                    ... @include(if: $v) @defer(if: $v) {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($v: Boolean!) {
              user(id: "1") {
                id
                ... @include(if: $v) @skip(if: $v) {
                  email
                }
                ... @include(if: $v) @defer(if: $v) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_MoveDirectivesToEnclosingFragment_When_SkipAndIncludeArePresent_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($s: Boolean!, $x: Boolean!, $v: Boolean!) {
                user(id: "1") {
                    id
                    ... on User @skip(if: $s) @include(if: $x) @defer(if: $v, label: "mail") {
                        email
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($s: Boolean!, $x: Boolean!, $v: Boolean!) {
              user(id: "1") {
                id
                ... on User @skip(if: $s) @include(if: $x) {
                  ... @skip(if: $v) {
                    email
                  }
                }
                ... on User @skip(if: $s) @include(if: $x) {
                  ... @include(if: $v) @defer(if: $v, label: "mail") {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafFromInnerFragment_When_OuterDeferredFragmentSelectsLeaf_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    ... @defer(label: "o") {
                        name
                        ... @defer(label: "i") {
                            name
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                ... @defer(label: "o") {
                  name
                  ... @defer(label: "i") {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineInnerFragment_When_OuterDeferredFragmentSelectsEveryLeaf_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    ... @defer(label: "o") {
                        name
                        ... @defer(label: "i") {
                            name
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                ... @defer(label: "o") {
                  name
                  ... {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepLeafInSiblingFragment_When_OtherDeferredFragmentSelectsLeaf_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    ... @defer(label: "a") {
                        name
                    }
                    ... @defer(label: "b") {
                        name
                        nick
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                ... @defer(label: "a") {
                  name
                }
                ... @defer(label: "b") {
                  name
                  nick
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafFromInnerFragmentInBothCopies_When_OuterFragmentIsSwitchable_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($o: Boolean!) {
                user(id: "1") {
                    ... @defer(if: $o, label: "o") {
                        name
                        ... @defer(label: "i") {
                            name
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($o: Boolean!) {
              user(id: "1") {
                ... @skip(if: $o) {
                  name
                  ... @defer(label: "i") {
                    nick
                  }
                }
                ... @include(if: $o) @defer(if: $o, label: "o") {
                  name
                  ... @defer(label: "i") {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepLeafInInnerFragment_When_OuterLeafIsConditional_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") {
                    ... @defer(label: "o") {
                        name @include(if: $x)
                        ... @defer(label: "i") {
                            name
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($x: Boolean!) {
              user(id: "1") {
                ... @defer(label: "o") {
                  name @include(if: $x)
                  ... @defer(label: "i") {
                    name
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepLeafInInnerFragment_When_OuterLeafIsInTypedFragment_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    ... @defer(label: "o") {
                        ... on User {
                            name
                        }
                        ... @defer(label: "i") {
                            name
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                ... @defer(label: "o") {
                  ... on User {
                    name
                  }
                  ... @defer(label: "i") {
                    name
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafInBothCopies_When_SwitchableFragmentIsInDeferredFragment_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query($o: Boolean!) {
                ... @defer(label: "d") {
                    user(id: "1") {
                        ... @defer(if: $o, label: "o") {
                            name
                            ... @defer(label: "i") {
                                name
                                nick
                            }
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            query($o: Boolean!) {
              ... @defer(label: "d") {
                user(id: "1") {
                  ... @skip(if: $o) {
                    name
                    ... @defer(label: "i") {
                      nick
                    }
                  }
                  ... @include(if: $o) @defer(if: $o, label: "o") {
                    name
                    ... @defer(label: "i") {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragment_When_OnlyInnerFragmentHasNewLeafAfterCoverage_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "o") {
                        name
                        ... @defer(label: "i") {
                            name
                            nick
                        }
                    }
                }
            }
            """;

        // act
        var lowered = LowerNotNormalized(operation);

        // assert
        lowered.MatchInlineSnapshot(
            """
            {
              user(id: "1") {
                name
                ... {
                  name
                  ... @defer(label: "i") {
                    nick
                  }
                }
              }
            }
            """);
    }

    private static string Lower(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var normalized = new DocumentRewriter(s_schema, mergeAcrossDefer: true)
            .RewriteDocument(document, operationName: null);
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
