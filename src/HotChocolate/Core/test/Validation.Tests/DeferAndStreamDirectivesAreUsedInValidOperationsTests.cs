using Microsoft.Extensions.DependencyInjection;

namespace HotChocolate.Validation;

public class DeferAndStreamDirectivesAreUsedInValidOperationsTests()
    : DocumentValidatorVisitorTestBase(builder => builder.AddSubscriptionDeferStreamRules())
{
    [Fact]
    public void Defer_Should_ReportError_When_NestedInSubscriptionWithoutIf()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Stream_Should_ReportError_When_IfIsTrueInSubscription()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                comments @stream(if: true) {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportError_When_IfIsTrueOnFragmentSpreadInSubscription()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... Body @defer(if: true)
              }
            }

            fragment Body on Message {
              body
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportError_When_InsideNamedFragmentOfSubscription()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... Message
              }
            }

            fragment Message on Message {
              ... @defer {
                body
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportError_When_InsideFragmentOfRootSelection()
    {
        ExpectErrors(
            """
            subscription {
              ... Root
            }

            fragment Root on Subscription {
              newMessage {
                ... @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportError_When_SkipIsFalse()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... @skip(if: false) @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportError_When_IncludeIsTrue()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... @include(if: true) @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Stream_Should_ReportError_When_WithoutIfInNestedSelection()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                author {
                  messages @stream {
                    body
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_ReportEachDirective_When_MultipleCannotBeDisabled()
    {
        ExpectErrors(
            """
            subscription {
              newMessage {
                ... @defer {
                  body
                }
                ... @defer(if: true) {
                  title
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_IfIsFalse()
    {
        ExpectValid(
            """
            subscription {
              newMessage {
                ... @defer(if: false) {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_IfIsVariable()
    {
        ExpectValid(
            """
            subscription($defer: Boolean!) {
              newMessage {
                ... @defer(if: $defer) {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Stream_Should_BeValid_When_IfIsVariable()
    {
        ExpectValid(
            """
            subscription($stream: Boolean!) {
              newMessage {
                comments @stream(if: $stream) {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_IfIsVariableOnFragmentSpread()
    {
        ExpectValid(
            """
            subscription($defer: Boolean!) {
              newMessage {
                ... Body @defer(if: $defer)
              }
            }

            fragment Body on Message {
              body
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_SelectionIsSkipped()
    {
        ExpectValid(
            """
            subscription {
              newMessage {
                ... @skip(if: true) @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_SelectionIsNotIncluded()
    {
        ExpectValid(
            """
            subscription {
              newMessage {
                ... @include(if: false) @defer {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_FieldWithDeferIsSkipped()
    {
        ExpectValid(
            """
            subscription($skip: Boolean!) {
              newMessage {
                author @skip(if: $skip) {
                  ... @defer {
                    name
                  }
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_OnRootSelectionOfSubscription()
    {
        ExpectValid(
            """
            subscription {
              ... @defer {
                newMessage {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_InQuery()
    {
        ExpectValid(
            """
            query {
              message {
                ... @defer {
                  body
                }
                comments @stream {
                  body
                }
              }
            }
            """);
    }

    [Fact]
    public void Defer_Should_BeValid_When_InMutation()
    {
        ExpectValid(
            """
            mutation {
              createMessage {
                ... @defer {
                  body
                }
              }
            }
            """);
    }
}
