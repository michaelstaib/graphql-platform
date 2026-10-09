using HotChocolate.Configuration;
using HotChocolate.Features;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;

namespace HotChocolate.Types.Pagination;

/// <summary>
/// Keeps the relative cursor fields of the <c>PageInfo</c> type out of the schema unless relative cursors
/// are enabled globally or on at least one field.
/// </summary>
internal sealed class PageInfoRelativeCursorFieldsTypeInterceptor : TypeInterceptor
{
    private const string PageInfoTypeName = "PageInfo";
    private const string ForwardCursorsFieldName = "forwardCursors";
    private const string BackwardCursorsFieldName = "backwardCursors";

    private readonly List<(int Index, ObjectFieldConfiguration Field)> _removedFields = [];
    private ObjectTypeConfiguration? _pageInfo;
    private bool _fieldEnabled;

    public override void OnBeforeRegisterDependencies(
        ITypeDiscoveryContext discoveryContext,
        TypeSystemConfiguration configuration)
    {
        switch (configuration)
        {
            case ObjectTypeConfiguration typeConfiguration:
                _fieldEnabled |= typeConfiguration.Fields.Any(t => IsEnabled(t.GetFeatures()));

                if (typeConfiguration.Name is PageInfoTypeName
                    && !IsEnabled(discoveryContext.DescriptorContext.Features))
                {
                    RemoveRelativeCursorFields(typeConfiguration);
                }

                break;

            case InterfaceTypeConfiguration interfaceConfiguration:
                _fieldEnabled |= interfaceConfiguration.Fields.Any(t => IsEnabled(t.GetFeatures()));
                break;
        }
    }

    public override IEnumerable<TypeReference> RegisterMoreTypes(
        IReadOnlyCollection<ITypeDiscoveryContext> discoveryContexts)
    {
        if (_pageInfo is null || _removedFields.Count == 0 || !_fieldEnabled)
        {
            yield break;
        }

        var pageInfo = _pageInfo;
        var restored = _removedFields.ToArray();
        _removedFields.Clear();

        foreach (var (index, field) in restored)
        {
            pageInfo.Fields.Insert(index, field);

            if (field.Type is { } type)
            {
                yield return type;
            }
        }
    }

    private void RemoveRelativeCursorFields(ObjectTypeConfiguration typeConfiguration)
    {
        _pageInfo = typeConfiguration;

        for (var i = typeConfiguration.Fields.Count - 1; i >= 0; i--)
        {
            var field = typeConfiguration.Fields[i];

            if (field.Name is ForwardCursorsFieldName or BackwardCursorsFieldName)
            {
                _removedFields.Insert(0, (i, field));
                typeConfiguration.Fields.RemoveAt(i);
            }
        }
    }

    private static bool IsEnabled(IFeatureCollection features)
        => features.TryGet<PagingOptions>(out var options) && options.EnableRelativeCursors is true;
}
