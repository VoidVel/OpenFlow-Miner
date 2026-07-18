using System.Reflection;
using OpenFlowMiner.Core.Domain;

namespace OpenFlowMiner.Core.Tests;

/// <summary>
/// The single most important test for this product's identity (Product Principle 4.1).
/// The core domain must be domain-agnostic by construction: it may never grow a field that
/// encodes an industry concept. A PR that adds <c>OrderStatus</c> or <c>ClaimType</c> fails here.
/// </summary>
public class DomainAgnosticGuardTests
{
    // The COMPLETE set of public data carried by the event atom. Extension happens only through
    // the generic Resource / Attributes escape hatches — never a typed, industry-specific field.
    private static readonly HashSet<string> AllowedEventMembers =
    [
        nameof(Event.CaseId),
        nameof(Event.Activity),
        nameof(Event.Timestamp),
        nameof(Event.Resource),
        nameof(Event.Attributes),
    ];

    // Words that would signal a vertical concept leaking into the core model.
    private static readonly string[] ForbiddenTerms =
    [
        "order", "claim", "ticket", "patient", "invoice", "customer", "loan",
        "payment", "product", "policy", "account", "shipment", "diagnosis",
    ];

    [Fact]
    public void Event_ExposesExactlyTheAgnosticFieldSet()
    {
        var actual = PublicDataProperties(typeof(Event)).Select(p => p.Name).ToHashSet();
        Assert.Equal(AllowedEventMembers, actual);
    }

    [Fact]
    public void NoDomainType_HasIndustrySpecificPropertyNames()
    {
        var offenders = new List<string>();

        foreach (var type in DomainTypes())
        {
            foreach (var property in PublicDataProperties(type))
            {
                if (ForbiddenTerms.Any(term => property.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    offenders.Add($"{type.Name}.{property.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Core domain model leaked industry-specific field(s): {string.Join(", ", offenders)}. " +
            "Such concepts belong in a downstream consumer, not in OpenFlowMiner.Core.");
    }

    private static IEnumerable<Type> DomainTypes()
        => typeof(Event).Assembly
            .GetTypes()
            .Where(t => t is { IsPublic: true, Namespace: "OpenFlowMiner.Core.Domain" });

    private static IEnumerable<PropertyInfo> PublicDataProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            // Ignore compiler-generated record plumbing (EqualityContract).
            .Where(p => p.Name != "EqualityContract");
}
