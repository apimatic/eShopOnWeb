using System;

namespace Microsoft.eShopWeb.PublicApi.Notifications;

/// <summary>Masks a phone number for display so a full contact number never leaves the system through an API
/// response, while keeping the last few digits so an operator can still tell messages apart.</summary>
internal static class PhoneMask
{
    public static string? Mask(string? number)
    {
        if (string.IsNullOrEmpty(number))
        {
            return number;
        }

        const int visible = 4;
        if (number.Length <= visible)
        {
            return new string('*', number.Length);
        }

        return string.Concat(new string('*', number.Length - visible), number.AsSpan(number.Length - visible));
    }
}
