using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public static class ProfileNames
{
    public static (string FirstName, string LastName) FromEmail(string email)
    {
        var emailAddress = email.Trim();
        var atIndex = emailAddress.IndexOf('@');
        var localPart = atIndex > 0 ? emailAddress[..atIndex] : emailAddress;
        var domain = atIndex > 0 ? emailAddress[(atIndex + 1)..] : string.Empty;

        var nameParts = localPart.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (nameParts.Length == 0)
        {
            nameParts = new[] { string.IsNullOrEmpty(domain) ? "User" : domain };
        }

        var firstName = nameParts[0];
        var lastName = nameParts.Length > 1 ? string.Join(" ", nameParts[1..]) : FirstDomainLabel(domain);
        return (firstName, lastName);
    }

    private static string FirstDomainLabel(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return "User";
        var label = domain.Split('.')[0];
        return string.IsNullOrEmpty(label) ? "User" : label;
    }
}
