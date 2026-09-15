using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

public static class PhoneNumberUsability
{
    private static readonly HashSet<string> BlockedLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager",
        "premium",
        "sharedCost",
        "uan",
        "tollFree"
    };

    public static bool IsUsableDestination(PhoneNumberLookupResult lookup, out string reason)
    {
        if (!lookup.Valid)
        {
            reason = lookup.ValidationErrors.Count > 0
                ? string.Join(", ", lookup.ValidationErrors)
                : "The provider does not consider this a usable destination.";
            return false;
        }

        if (string.IsNullOrEmpty(lookup.CanonicalPhoneNumber))
        {
            reason = "The provider did not return a canonical phone number.";
            return false;
        }

        // A package-level failure (for example 60601 on Canadian numbers) is not a
        // verdict on the number. Validity is then the only signal we have.
        if (lookup.LineTypeErrorCode is not null)
        {
            reason = string.Empty;
            return true;
        }

        if (!string.IsNullOrEmpty(lookup.LineType) && BlockedLineTypes.Contains(lookup.LineType))
        {
            reason = $"Line type '{lookup.LineType}' cannot receive SMS.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
