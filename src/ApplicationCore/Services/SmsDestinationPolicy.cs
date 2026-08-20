using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class SmsDestinationPolicy
{
    private static readonly HashSet<string> UnusableLineTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "landline",
        "voicemail",
        "pager"
    };

    public static bool IsUsableSmsDestination(string? lineType, int? lineTypeErrorCode, bool valid)
    {
        if (!valid)
        {
            return false;
        }

        // Package-level failures (for example Canadian NPAC 60601) leave validity intact
        // and must not reject a number the basic lookup already accepted.
        if (lineTypeErrorCode is not null || string.IsNullOrWhiteSpace(lineType))
        {
            return true;
        }

        return !UnusableLineTypes.Contains(lineType);
    }
}
