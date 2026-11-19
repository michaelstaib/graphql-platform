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
            users: [User!]!
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
            profiles: [Profile!]!
            friends: [User!]!
            manager: User
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
                ... @defer(label: "mail", __group: 0, __parent: -1) {
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
                ... @include(if: $d) @defer(if: $d, label: "mail", __group: 0, __parent: -1) {
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
                  ...
                    @include(if: $b)
                    @defer(if: $b, label: "inner", __group: 0, __parent: -1) {
                    email
                  }
                }
                ...
                  @include(if: $a)
                  @defer(if: $a, label: "outer", __group: 1, __parent: -1) {
                  name
                  ... @skip(if: $b) {
                    email
                  }
                  ...
                    @include(if: $b)
                    @defer(if: $b, label: "inner", __group: 2, __parent: 1) {
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
                ... @defer(label: "outer", __group: 0, __parent: -1) {
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
                ... @defer(__group: 0, __parent: -1) {
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
              ... @include(if: $d) @defer(if: $d, __group: 0, __parent: -1) {
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
                  ... @include(if: $d) @defer(if: $d, __group: 0, __parent: -1) {
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
                ... @skip(if: $s) @include(if: $d) @defer(if: $d, __group: 0, __parent: -1) {
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
                ... @defer(__group: 0, __parent: -1) {
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
                ... @defer(__group: 0, __parent: -1) {
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
                ... @defer(__group: 0, __parent: -1) {
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
                ... @defer(__group: 0, __parent: -1) {
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
                  ... @defer(label: "inner", __group: 0, __parent: -1) {
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
                  ... @defer(label: "inner", __group: 0, __parent: -1) {
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
                  ... @defer(label: "inner", __group: 0, __parent: -1) {
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
                ... @defer(label: "outer", __group: 0, __parent: -1) {
                  ... @skip(if: $c) {
                    email
                  }
                  ...
                    @include(if: $c)
                    @defer(if: $c, label: "inner", __group: 1, __parent: 0) {
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
                ... @defer(label: "outer", __group: 0, __parent: -1) {
                  friends {
                    ... @defer(label: "inner", __group: 1, __parent: 0) {
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
                    ... @defer(label: "inner", __group: 0, __parent: -1) {
                      name
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OnlyTypenameIsSelectedAndAnchorTypeHasLookup()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "t") {
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
                name
                ... {
                  __typename
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_SelectTypenameOnceWithoutCondition_When_SwitchableFragmentSelectsOnlyTypename()
    {
        // arrange
        const string operation =
            """
            query($v: Boolean!) {
                user(id: "1") {
                    name
                    ... @defer(if: $v, label: "t") {
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
            query($v: Boolean!) {
              user(id: "1") {
                name
                ... {
                  __typename
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OnlyTypenameIsLeftAfterTheOwnFieldIsPruned()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer {
                        name
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
                name
                ... {
                  name
                  __typename
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragmentAndKeepInnerFragment_When_OuterSelectsOnlyTypenameAndNestedFragment()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "o") {
                        __typename
                        ... @defer(label: "i") {
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
                name
                ... {
                  __typename
                  ... @defer(label: "i", __group: 0, __parent: -1) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferred_When_TypenameIsSelectedNextToOtherOwnField()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "t") {
                        __typename
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
                name
                ... @defer(label: "t", __group: 0, __parent: -1) {
                  __typename
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferred_When_CompositeFieldSelectsOnlyTypename()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "t") {
                        profile {
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
                name
                ... @defer(label: "t", __group: 0, __parent: -1) {
                  profile {
                    __typename
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OnlyAliasedTypenameIsSelected()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    ... @defer(label: "t") {
                        t: __typename
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
                name
                ... {
                  t: __typename
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
                  ...
                    @include(if: $v)
                    @defer(if: $v, label: "inner", __group: 0, __parent: -1) {
                    nick
                  }
                }
                ...
                  @include(if: $v)
                  @defer(if: $v, label: "outer", __group: 1, __parent: -1) {
                  email
                  ... @skip(if: $v) {
                    nick
                  }
                  ...
                    @include(if: $v)
                    @defer(if: $v, label: "inner", __group: 2, __parent: 1) {
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
                  ... @include(if: $v) @defer(if: $v, __group: 0, __parent: -1) {
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
                  ... @include(if: $v) @defer(if: $v, __group: 0, __parent: -1) {
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
                ... @include(if: $v) @defer(if: $v, __group: 0, __parent: -1) {
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
                  ...
                    @include(if: $v)
                    @defer(if: $v, label: "mail", __group: 0, __parent: -1) {
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
                ... @defer(label: "o", __group: 0, __parent: -1) {
                  name
                  ... @defer(label: "i", __group: 1, __parent: 0) {
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
                ... @defer(label: "o", __group: 0, __parent: -1) {
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
                ... @defer(label: "a", __group: 0, __parent: -1) {
                  name
                }
                ... @defer(label: "b", __group: 1, __parent: -1) {
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
                  ... @defer(label: "i", __group: 0, __parent: -1) {
                    nick
                  }
                }
                ... @include(if: $o) @defer(if: $o, label: "o", __group: 1, __parent: -1) {
                  name
                  ... @defer(label: "i", __group: 2, __parent: 1) {
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
                ... @defer(label: "o", __group: 0, __parent: -1) {
                  name @include(if: $x)
                  ... @defer(label: "i", __group: 1, __parent: 0) {
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
                ... @defer(label: "o", __group: 0, __parent: -1) {
                  ... on User {
                    name
                  }
                  ... @defer(label: "i", __group: 1, __parent: 0) {
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
              ... @defer(label: "d", __group: 0, __parent: -1) {
                user(id: "1") {
                  ... @skip(if: $o) {
                    name
                    ... @defer(label: "i", __group: 1, __parent: 0) {
                      nick
                    }
                  }
                  ... @include(if: $o) @defer(if: $o, label: "o", __group: 2, __parent: 0) {
                    name
                    ... @defer(label: "i", __group: 3, __parent: 2) {
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
                  ... @defer(label: "i", __group: 0, __parent: -1) {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_DropLeafFromInnerFragment_When_OuterFragmentSelectsLeafBelowField_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                ... @defer(label: "o") {
                    user(id: "1") {
                        id
                        ... @defer(label: "i") {
                            id
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
              ... @defer(label: "o", __group: 0, __parent: -1) {
                user(id: "1") {
                  id
                  ... @defer(label: "i", __group: 1, __parent: 0) {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineInnerFragment_When_OuterFragmentSelectsEveryLeafBelowField_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                ... @defer(label: "o") {
                    user(id: "1") {
                        id
                        ... @defer(label: "i") {
                            id
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
              ... @defer(label: "o", __group: 0, __parent: -1) {
                user(id: "1") {
                  id
                  ... {
                    id
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OnlyIntrospectionIsSelectedAtTheRoot()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                }
                ... @defer(label: "i") {
                    __type(name: "User") {
                        name
                    }
                    __schema {
                        queryType {
                            name
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
                name
              }
              ... {
                __type(name: "User") {
                  name
                }
                __schema {
                  queryType {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferredWithItsIntrospection_When_IntrospectionIsSelectedNextToALookupField()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                }
                ... @defer(label: "i") {
                    __type(name: "User") {
                        name
                    }
                    user(id: "1") {
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
                name
              }
              ... @defer(label: "i", __group: 0, __parent: -1) {
                __type(name: "User") {
                  name
                }
                user(id: "1") {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_PlainAndDeferredSelectTheSameIntrospectionField()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                }
                __type(name: "User") {
                    name
                }
                ... @defer(label: "i") {
                    __type(name: "User") {
                        kind
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
                name
              }
              __type(name: "User") {
                name
              }
              ... {
                __type(name: "User") {
                  kind
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOuterFragmentAndKeepInnerFragment_When_OuterSelectsOnlyIntrospection()
    {
        // arrange
        const string operation =
            """
            query {
                ... @defer(label: "o") {
                    __type(name: "User") {
                        name
                    }
                    ... @defer(label: "i") {
                        user(id: "1") {
                            name
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
              ... {
                __type(name: "User") {
                  name
                }
                ... @defer(label: "i", __group: 0, __parent: -1) {
                  user(id: "1") {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_TheSameIntrospectionFieldIsSelectedOutsideAndInsideTheFragment()
    {
        // arrange
        const string operation =
            """
            query {
                __type(name: "User") {
                    name
                }
                ... @defer(label: "i") {
                    __type(name: "User") {
                        name
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
              __type(name: "User") {
                name
              }
              ... {
                __type(name: "User") {
                  name
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AttachPartToRootList_When_FragmentSelectsListOfTheRootSelection()
    {
        // arrange
        const string operation =
            """
            query {
                users {
                    name
                }
                ... @defer(label: "top") {
                    users {
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
              users {
                name
                ... @defer(label: "top", __group: 0, __parent: -1, __levels: 1) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AttachPartToList_When_FragmentSelectsListOfTheEnclosingObject()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    name
                    friends {
                        id
                    }
                    ... @defer(label: "b") {
                        friends {
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
                name
                friends {
                  id
                  ... @defer(label: "b", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepOwnFieldsAtThePlace_When_FragmentAlsoHasAPart()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(label: "b") {
                        total
                        friends {
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
                friends {
                  id
                  ... @defer(label: "b", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
                ... @defer(label: "b", __group: 0, __parent: -1) {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_NumberGroupsInDocumentOrderOfTheirPlaces_When_AFragmentHasOnlyParts()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(label: "x") {
                        friends {
                            email
                        }
                    }
                    ... @defer(label: "y") {
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
                friends {
                  id
                  ... @defer(label: "x", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
                ... @defer(label: "y", __group: 1, __parent: -1) {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_ReplaceInternalArguments_When_TheClientSendsThem()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    id
                    ... @defer(label: "mail", __group: 9, __parent: 8, __levels: 7) {
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
                ... @defer(label: "mail", __group: 0, __parent: -1) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_CutAtTheDeepestCoveredList_When_ListsAreNested()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        id
                        friends {
                            id
                        }
                    }
                    ... @defer(label: "n") {
                        friends {
                            email
                            friends {
                                total
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
                friends {
                  id
                  friends {
                    id
                    ... @defer(label: "n", __group: 0, __parent: -1, __levels: 2) {
                      total
                    }
                  }
                  ... @defer(label: "n", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AttachPartToTheListOfTheEnclosingFragment_When_FragmentIsNestedInADeferredFragment()
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
                        total
                        ... @defer(label: "inner") {
                            friends {
                                email
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
                friends {
                  id
                  ... @defer(label: "inner", __group: 1, __parent: 0, __levels: 1) {
                    email
                  }
                }
                ... @defer(label: "outer", __group: 0, __parent: -1) {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_CarryTheConditionsOfTheCutFields_When_TheListIsSelectedUnderACondition()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(label: "b") {
                        friends @include(if: $x) {
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
            query($x: Boolean!) {
              user(id: "1") {
                friends {
                  id
                  ... @include(if: $x) {
                    ... @defer(label: "b", __group: 0, __parent: -1, __levels: 1) {
                      email
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_AttachPartToTheDeferredCopy_When_FragmentIsSwitchable()
    {
        // arrange
        const string operation =
            """
            query($d: Boolean!) {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(if: $d, label: "b") {
                        friends {
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
            query($d: Boolean!) {
              user(id: "1") {
                friends {
                  id
                  ...
                    @include(if: $d)
                    @defer(if: $d, label: "b", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
                ... @skip(if: $d) {
                  friends {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_ItemTypeOfTheListHasNoLookup()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    profiles {
                        bio
                    }
                    ... @defer(label: "b") {
                        profiles {
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
                profiles {
                  bio
                }
                ... {
                  profiles {
                    avatar
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_OutsideSelectionOfTheListHasAnIncludeCondition()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") {
                    friends @include(if: $x) {
                        id
                    }
                    ... @defer(label: "b") {
                        friends {
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
            query($x: Boolean!) {
              user(id: "1") {
                friends @include(if: $x) {
                  id
                }
                ... {
                  friends {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_FragmentHasATypeCondition()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... on User @defer(label: "b") {
                        friends {
                            email
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
                ... on User {
                  friends {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_ATypeConditionIsBetweenTheFragmentAndTheList()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(label: "b") {
                        ... on User {
                            friends {
                                email
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
                  ... on User {
                    friends {
                      email
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentUnchanged_When_TheCoveredFieldIsNotAList()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    profile {
                        bio
                    }
                    ... @defer(label: "b") {
                        total
                        profile {
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
                }
                ... @defer(label: "b", __group: 0, __parent: -1) {
                  total
                  profile {
                    avatar
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineInnerFragment_When_ASwitchableFragmentStandsBetweenItAndTheList()
    {
        // arrange
        const string operation =
            """
            query($d: Boolean!) {
                user(id: "1") {
                    friends {
                        id
                    }
                    ... @defer(if: $d, label: "outer") {
                        total
                        ... @defer(label: "inner") {
                            friends {
                                email
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
            query($d: Boolean!) {
              user(id: "1") {
                friends {
                  id
                  ...
                    @include(if: $d)
                    @defer(if: $d, label: "outer", __group: 0, __parent: -1, __levels: 1) {
                    email
                  }
                }
                ... @skip(if: $d) {
                  total
                  ... {
                    friends {
                      email
                    }
                  }
                }
                ...
                  @include(if: $d)
                  @defer(if: $d, label: "outer", __group: 0, __parent: -1) {
                  total
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_NumberTheParentBeforeItsChild_When_TheNestedFragmentStandsInAPart()
    {
        // arrange
        const string operation =
            """
            query {
                users {
                    name
                }
                ... @defer(label: "outer") {
                    users {
                        email
                        ... @defer(label: "inner") {
                            total
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
              users {
                name
                ... @defer(label: "outer", __group: 0, __parent: -1, __levels: 1) {
                  email
                  ... @defer(label: "inner", __group: 1, __parent: 0) {
                    total
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_ATypedFragmentSelectsTheListBelowTheCoveredList_InputIsNotNormalized()
    {
        // arrange
        const string operation =
            """
            query {
                users {
                    ... on User {
                        name
                        friends {
                            id
                        }
                    }
                }
                ... @defer(label: "d") {
                    users {
                        ... on User {
                            friends {
                                email
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
              users {
                ... on User {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                users {
                  ... on User {
                    friends {
                      email
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineFragment_When_TheObjectAboveTheListIsSelectedOutsideUnderACondition()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    name
                    friends {
                        id
                    }
                }
                ... @defer(label: "d") {
                    user(id: "1") {
                        friends {
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepFragmentDeferred_When_TheConditionalObjectOutsideDoesNotSelectTheList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    name
                }
                ... @defer(label: "d") {
                    user(id: "1") {
                        friends {
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                name
              }
              ... @defer(label: "d", __group: 0, __parent: -1) {
                user(id: "1") {
                  friends {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheNestedFragment_When_ItSelectsAListOfTheOutsideSelectionBelowACoveredField()
    {
        // arrange
        const string operation =
            """
            query {
                users {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "i") {
                            friends { total }
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
              users {
                name
                friends {
                  id
                  ... @defer(label: "o", __group: 0, __parent: -1, __levels: 2) {
                    total
                  }
                }
                ... @defer(label: "o", __group: 0, __parent: -1, __levels: 1) {
                  email
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheEnclosingFragments_When_AFragmentBelowThemSelectsAListOfTheOutsideSelection()
    {
        // arrange
        const string operation =
            """
            query {
                users {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "i") {
                            nick
                            ... @defer(label: "k") {
                                friends { total }
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
              users {
                name
                friends {
                  id
                  ... @defer(label: "o", __group: 0, __parent: -1, __levels: 2) {
                    total
                  }
                }
                ... @defer(label: "o", __group: 0, __parent: -1, __levels: 1) {
                  email
                  ... {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheNestedFragment_When_ItSelectsAListOfAConditionalOutsideSelection()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "i") {
                            friends { total }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    friends {
                      total
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheNestedFragment_When_ItSelectsOnlyALeafBelowAConditionalOutsideSelection()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "i") {
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... @defer(label: "i", __group: 0, __parent: -1) {
                    nick
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheNestedFragment_When_AnObjectLiesBetweenItAndAConditionalOutsideSelection()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends {
                            email
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                    ... {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheNestedFragment_When_ItSelectsOnlyALeafBelowAConditionalOutsideSelectionObject()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends {
                            email
                            ... @defer(label: "i") {
                                nick
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                    ... @defer(label: "i", __group: 0, __parent: -1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheNestedFragment_When_ItSelectsAListTheConditionalOutsideSelectionLacks()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends {
                            email
                            ... @defer(label: "i") {
                                profiles { bio }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                    ... @defer(label: "i", __group: 0, __parent: -1) {
                      profiles {
                        bio
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOnlyTheListFragment_When_TwoNestedFragmentsLieBelowAnObjectOfAConditionalSelection()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends {
                            email
                            ... @defer(label: "i") {
                                friends { total }
                            }
                            ... @defer(label: "j") {
                                nick
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                    ... {
                      friends {
                        total
                      }
                    }
                    ... @defer(label: "j", __group: 0, __parent: -1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineBothNestedFragments_When_OneOnlyWrapsTheOneThatSelectsAConditionalList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "m") {
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    ... {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheInnerFragment_When_AFragmentOnlyWrapsItAndItSelectsOnlyALeaf()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "m") {
                            ... @defer(label: "i") {
                                nick
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    ... @defer(label: "i", __group: 0, __parent: -1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheWrappingFragment_When_ItHasAnOwnFieldBesideTheNestedFragmentWithTheList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "m") {
                            nick
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    nick
                    ... {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheFragments_When_TheNestedFragmentSelectsAListBelowAConditionalOutsideObject()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    manager {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        manager {
                            email
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                manager {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  manager {
                    email
                    ... {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheFragments_When_TheConditionalOutsideFieldLiesBelowAFieldThatIsAlwaysSelected()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") {
                    id
                    manager @include(if: $x) {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        manager {
                            email
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              user(id: "1") {
                id
                manager @include(if: $x) {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  manager {
                    email
                    ... {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineOnlyTheListFragment_When_TwoNestedFragmentsLieBelowAConditionalOutsideObject()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    manager {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        manager {
                            email
                            ... @defer(label: "i") {
                                friends { total }
                            }
                            ... @defer(label: "j") {
                                nick
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                manager {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  manager {
                    email
                    ... {
                      friends {
                        total
                      }
                    }
                    ... @defer(label: "j", __group: 0, __parent: -1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheNestedFragment_When_ItSelectsOnlyALeafBelowAnObjectOfAConditionalOutsideSelection()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    manager {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        manager {
                            email
                            ... @defer(label: "i") {
                                nick
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                manager {
                  name
                  friends {
                    id
                  }
                }
              }
              ... @defer(label: "o", __group: 0, __parent: -1) {
                user(id: "1") {
                  manager {
                    email
                    ... @defer(label: "i", __group: 1, __parent: 0) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheNestedFragment_When_TheConditionalOutsideSelectionBelowAnObjectLacksItsList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    manager { name }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        manager {
                            email
                            ... @defer(label: "i") {
                                friends { total }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                manager {
                  name
                }
              }
              ... @defer(label: "o", __group: 0, __parent: -1) {
                user(id: "1") {
                  manager {
                    email
                    ... @defer(label: "i", __group: 1, __parent: 0) {
                      friends {
                        total
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheFragments_When_AWrapperHoldsTheNestedFragmentBelowAConditionalOutsideList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends { email }
                        ... @defer(label: "m") {
                            friends {
                                ... @defer(label: "i") {
                                    friends { total }
                                }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                  }
                  ... {
                    friends {
                      ... {
                        friends {
                          total
                        }
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheWrapperAndTheNestedFragment_When_ThePartSelectsAnOutsideList()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends { email }
                        ... @defer(label: "m") {
                            friends {
                                ... @defer(label: "i") {
                                    friends { total }
                                }
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
                friends {
                  name
                  friends {
                    id
                    ... @defer(label: "o", __group: 0, __parent: -1, __levels: 3) {
                      total
                    }
                  }
                  ... @defer(label: "o", __group: 0, __parent: -1, __levels: 2) {
                    email
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepThePartOfTheWrapper_When_ItSelectsOnlyALeafBelowAConditionalOutsideList()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                user(id: "1") @include(if: $x) {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends { email }
                        ... @defer(label: "m") {
                            friends { nick }
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
            query($x: Boolean!) {
              user(id: "1") @include(if: $x) {
                friends {
                  name
                  friends {
                    id
                  }
                }
              }
              ... {
                user(id: "1") {
                  friends {
                    email
                    ... @defer(label: "m", __group: 0, __parent: -1, __levels: 1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepThePartOfTheWrapper_When_ItSelectsOnlyALeafBelowAnOutsideList()
    {
        // arrange
        const string operation =
            """
            query {
                user(id: "1") {
                    friends {
                        name
                        friends { id }
                    }
                }
                ... @defer(label: "o") {
                    user(id: "1") {
                        friends { email }
                        ... @defer(label: "m") {
                            friends { nick }
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
                friends {
                  name
                  friends {
                    id
                  }
                  ... @defer(label: "o", __group: 0, __parent: -1, __levels: 2) {
                    email
                    ... @defer(label: "m", __group: 1, __parent: 0, __levels: 1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheWrappers_When_AWrapperChainIsNested3LevelsDeep()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "w2") {
                            ... @defer(label: "w1") {
                                ... @defer(label: "w0") {
                                    ... @defer(label: "i") {
                                        friends { total }
                                    }
                                }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    ... {
                      ... {
                        ... {
                          friends {
                            total
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_KeepTheLeafFragments_When_EveryLevelOfAChainHasALeafFragment()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "w2") {
                            ... @defer(label: "w1") {
                                ... @defer(label: "w0") {
                                    ... @defer(label: "i") {
                                        friends { total }
                                    }
                                    ... @defer(label: "l0") { nick }
                                }
                                ... @defer(label: "l1") { nick }
                            }
                            ... @defer(label: "l2") { nick }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    ... {
                      ... {
                        ... {
                          friends {
                            total
                          }
                        }
                        ... @defer(label: "l0", __group: 0, __parent: -1) {
                          nick
                        }
                      }
                      ... @defer(label: "l1", __group: 1, __parent: -1) {
                        nick
                      }
                    }
                    ... @defer(label: "l2", __group: 2, __parent: -1) {
                      nick
                    }
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Lower_Should_InlineTheSiblingPairs_When_SiblingPairsAreNested3LevelsDeep()
    {
        // arrange
        const string operation =
            """
            query($x: Boolean!) {
                users @include(if: $x) {
                    name
                    friends { id }
                }
                ... @defer(label: "o") {
                    users {
                        email
                        ... @defer(label: "a2") {
                            ... @defer(label: "a1") {
                                ... @defer(label: "a0") {
                                    ... @defer(label: "i") {
                                        friends { total }
                                    }
                                }
                                ... @defer(label: "b0") {
                                    ... @defer(label: "c0") { friends { email } }
                                }
                            }
                            ... @defer(label: "b1") {
                                ... @defer(label: "c1") { friends { email } }
                            }
                        }
                        ... @defer(label: "b2") {
                            ... @defer(label: "c2") { friends { email } }
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
            query($x: Boolean!) {
              users @include(if: $x) {
                name
                friends {
                  id
                }
              }
              ... {
                users {
                  email
                  ... {
                    ... {
                      ... {
                        ... {
                          friends {
                            total
                          }
                        }
                      }
                      ... {
                        ... {
                          friends {
                            email
                          }
                        }
                      }
                    }
                    ... {
                      ... {
                        friends {
                          email
                        }
                      }
                    }
                  }
                  ... {
                    ... {
                      friends {
                        email
                      }
                    }
                  }
                }
              }
            }
            """);
    }

    [Theory]
    [InlineData("wrapper-chain", 66, 0)]
    [InlineData("leaf-per-level", 66, 64)]
    [InlineData("sibling-pairs", 194, 0)]
    public async Task Lower_Should_Complete_When_DeferFragmentsAreNested64LevelsDeep(
        string shape,
        int expectedPlainFragments,
        int expectedDeferredFragments)
    {
        // arrange
        var operation = CreateNestedDefer(shape, 64);
        var cancellationToken = TestContext.Current.CancellationToken;

        // act
        var lowering = Task.Run(() => Lower(operation), cancellationToken);
        var finished = await Task.WhenAny(lowering, Task.Delay(TimeSpan.FromMinutes(2), cancellationToken));

        // assert
        Assert.Same(lowering, finished);
        var lowered = await lowering;
        Assert.Equal(expectedPlainFragments, CountOccurrences(lowered, "... {"));
        Assert.Equal(expectedDeferredFragments, CountOccurrences(lowered, "@defer("));
    }

    private static string CreateNestedDefer(string shape, int depth)
    {
        var nested = "... @defer(label: \"i\") { friends { total } }";

        for (var i = 0; i < depth; i++)
        {
            nested = shape switch
            {
                "wrapper-chain" => $"... @defer(label: \"w{i}\") {{ {nested} }}",
                "leaf-per-level" =>
                    $"... @defer(label: \"w{i}\") {{ {nested} ... @defer(label: \"l{i}\") {{ nick }} }}",
                _ => $"... @defer(label: \"a{i}\") {{ {nested} }} "
                    + $"... @defer(label: \"b{i}\") {{ ... @defer(label: \"c{i}\") {{ friends {{ email }} }} }}"
            };
        }

        return "query($x: Boolean!) { users @include(if: $x) { name friends { id } } "
            + $"... @defer(label: \"o\") {{ users {{ email {nested} }} }} }}";
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string Lower(string operationText)
    {
        var document = Utf8GraphQLParser.Parse(operationText);
        var normalized = new DocumentRewriter(
                s_schema,
                mergeAcrossDefer: false,
                removeStaticallyExcludedSelections: true)
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
