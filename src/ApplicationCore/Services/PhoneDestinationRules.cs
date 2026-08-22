using System;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public static class PhoneDestinationRules
{
    public const string MobileLineType = "mobile";

    private static readonly string[] NonSmsLineTypes =
    {
        "landline",
        "pager",
        "voicemail",
        "uan",
        "premium",
        "sharedCost"
    };

    public static bool IsUsableSmsDestination(bool? valid, string? lineType, int? lineTypeErrorCode)
    {
        if (valid != true)
        {
            return false;
        }

        if (lineTypeErrorCode is not null || string.IsNullOrWhiteSpace(lineType))
        {
            return true;
        }

        if (string.Equals(lineType, MobileLineType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (NonSmsLineTypes.Any(t => string.Equals(t, lineType, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }
}

