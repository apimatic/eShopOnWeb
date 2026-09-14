using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Translates between the Maxio SDK's generated enums and the wire vocabulary Maxio itself uses.
/// The SDK decorates every member with <see cref="EnumMemberAttribute"/> (for example
/// <c>SubscriptionState.PastDue</c> is <c>past_due</c>), which is the vocabulary eShopOnWeb surfaces to
/// its own callers so the API never invents a second set of state names.
/// </summary>
internal static class MaxioEnum
{
    private static readonly ConcurrentDictionary<Type, ConcurrentDictionary<string, string>> WireNames = new();

    /// <summary>Returns the wire value for an enum member, e.g. "past_due"; null for a null value.</summary>
    public static string? ToWireName<TEnum>(TEnum? value) where TEnum : struct, Enum =>
        value.HasValue ? ToWireName(value.Value) : null;

    /// <summary>Returns the wire value for an enum member, e.g. "past_due".</summary>
    public static string ToWireName<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var cache = WireNames.GetOrAdd(typeof(TEnum), _ => new ConcurrentDictionary<string, string>(StringComparer.Ordinal));
        var name = value.ToString();

        return cache.GetOrAdd(name, static (memberName, enumType) =>
        {
            var attribute = enumType.GetField(memberName, BindingFlags.Public | BindingFlags.Static)
                ?.GetCustomAttribute<EnumMemberAttribute>();

            return attribute?.Value ?? memberName.ToLowerInvariant();
        }, typeof(TEnum));
    }

    /// <summary>Parses a wire value (e.g. "remittance") back into its enum member; null when unrecognised.</summary>
    public static TEnum? FromWireName<TEnum>(string? wireName) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(wireName))
        {
            return null;
        }

        var trimmed = wireName.Trim();

        foreach (var candidate in Enum.GetValues(typeof(TEnum)).Cast<TEnum>())
        {
            if (string.Equals(ToWireName(candidate), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Every wire value of an enum, for use in error messages.</summary>
    public static string[] WireNamesOf<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues(typeof(TEnum)).Cast<TEnum>().Select(ToWireName).ToArray();
}
