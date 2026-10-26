using HotChocolate.Language;
using HotChocolate.Language.Visitors;
using HotChocolate.Types;
using static HotChocolate.Language.SyntaxKind;

namespace HotChocolate.Validation.Rules;

/// <summary>
/// The @defer and @stream directives can not be used to defer or stream data in
/// subscription operations. If these directives appear in a subscription operation
/// they must be disabled using an if argument.
///
/// https://spec.graphql.org/draft/#sec-Defer-And-Stream-Directives-Are-Used-In-Valid-Operations
/// </summary>
internal sealed class SubscriptionDeferStreamVisitor : DocumentValidatorVisitor
{
    protected override ISyntaxVisitorAction Enter(
        OperationDefinitionNode node,
        DocumentValidatorContext context)
        => node.Operation is OperationType.Subscription ? Continue : Skip;

    protected override ISyntaxVisitorAction Enter(
        FieldNode node,
        DocumentValidatorContext context)
        => ValidateSelection(node.Directives, context);

    protected override ISyntaxVisitorAction Enter(
        InlineFragmentNode node,
        DocumentValidatorContext context)
        => ValidateSelection(node.Directives, context);

    protected override ISyntaxVisitorAction Enter(
        FragmentSpreadNode node,
        DocumentValidatorContext context)
        => ValidateSelection(node.Directives, context);

    private static ISyntaxVisitorAction ValidateSelection(
        IReadOnlyList<DirectiveNode> directives,
        DocumentValidatorContext context)
    {
        if (directives.Count == 0)
        {
            return Continue;
        }

        if (IsConditionallyExcluded(directives))
        {
            return Skip;
        }

        // Selections that are not nested in a field are the root selections of the subscription.
        // They are rejected by the rule for root fields.
        if (IsNestedInField(context))
        {
            for (var i = 0; i < directives.Count; i++)
            {
                var directive = directives[i];

                if (IsDeferOrStream(directive) && !CanBeDisabled(directive))
                {
                    context.ReportError(context.DeferAndStreamNotDisableableInSubscription(directive));
                }
            }
        }

        return Continue;
    }

    private static bool IsConditionallyExcluded(IReadOnlyList<DirectiveNode> directives)
    {
        for (var i = 0; i < directives.Count; i++)
        {
            var directive = directives[i];

            var name = directive.Name.Value;
            var condition = directive.GetArgumentValue(DirectiveNames.Skip.Arguments.If);

            if (name.Equals(DirectiveNames.Skip.Name, StringComparison.Ordinal)
                && condition is not BooleanValueNode { Value: false })
            {
                return true;
            }

            if (name.Equals(DirectiveNames.Include.Name, StringComparison.Ordinal)
                && condition is not BooleanValueNode { Value: true })
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeferOrStream(DirectiveNode directive)
        => directive.Name.Value.Equals(DirectiveNames.Defer.Name, StringComparison.Ordinal)
            || directive.Name.Value.Equals(DirectiveNames.Stream.Name, StringComparison.Ordinal);

    private static bool CanBeDisabled(DirectiveNode directive)
        => directive.GetArgumentValue(DirectiveNames.Defer.Arguments.If)
            is not (null or BooleanValueNode { Value: true });

    private static bool IsNestedInField(DocumentValidatorContext context)
    {
        var path = context.Path;

        for (var i = 0; i < path.Count; i++)
        {
            if (path[i].Kind == Field)
            {
                return true;
            }
        }

        return false;
    }
}
