using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using AdvancedBilling.Standard.Models;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Reads the wire value behind an SDK enum, and classifies subscription states.
/// </summary>
internal static class MaxioEnums
{
    private static readonly Dictionary<Type, Dictionary<int, string>> WireValues = new();
    private static readonly object Sync = new();

    /// <summary>
    /// Returns the value Advanced Billing uses on the wire for <paramref name="value"/>, e.g.
    /// <c>past_due</c> for <see cref="SubscriptionState.PastDue"/>.
    /// </summary>
    /// <remarks>
    /// Taken from the <see cref="EnumMemberAttribute"/> the SDK generates, so the strings this
    /// integration hands to clients are exactly the ones Advanced Billing documents — no second,
    /// hand-maintained copy of the vocabulary that could drift.
    /// </remarks>
    public static string ToWireValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var map = GetMap(typeof(TEnum));
        return map.TryGetValue(Convert.ToInt32(value), out var wire) ? wire : value.ToString();
    }

    public static string? ToWireValueOrNull<TEnum>(TEnum? value)
        where TEnum : struct, Enum
        => value.HasValue ? ToWireValue(value.Value) : null;

    /// <summary>
    /// True when a subscription in <paramref name="state"/> still stands between the shopper and
    /// re-subscribing to the same plan.
    /// </summary>
    /// <remarks>
    /// Advanced Billing groups <c>on_hold</c> and <c>suspended</c> under "End of Life", but both are
    /// expected to resume, so they are treated as live here: enrolling a shopper a second time while one
    /// of those is outstanding would leave them paying twice once it resumes. Only the states a
    /// subscription can never come back from allow a fresh enrolment.
    /// </remarks>
    public static bool IsLive(SubscriptionState? state) => state switch
    {
        null => false,
        SubscriptionState.Canceled => false,
        SubscriptionState.Expired => false,
        SubscriptionState.FailedToCreate => false,
        SubscriptionState.TrialEnded => false,
        _ => true,
    };

    private static Dictionary<int, string> GetMap(Type enumType)
    {
        lock (Sync)
        {
            if (WireValues.TryGetValue(enumType, out var cached))
            {
                return cached;
            }

            var map = new Dictionary<int, string>();

            foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var attribute = field.GetCustomAttribute<EnumMemberAttribute>();
                var key = Convert.ToInt32(field.GetValue(null));
                map[key] = attribute?.Value ?? field.Name;
            }

            WireValues[enumType] = map;
            return map;
        }
    }
}
