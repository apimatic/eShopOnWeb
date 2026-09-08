namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

internal static class EmailNameParser
{
    /// <summary>
    /// Derives a first/last name pair from an e-mail address. The eShopOnWeb identity does not
    /// collect real names, so the billing record uses the mailbox part of the address: a dotted
    /// local part yields "jane.doe@example.com" → Jane / Doe, while a bare local part falls back
    /// to the address domain ("demouser@contoso.com" → demouser / contoso).
    /// </summary>
    public static (string FirstName, string LastName) Parse(string email)
    {
        var at = email.LastIndexOf('@');
        string local = at >= 0 ? email[..at] : email;
        string domain = at >= 0 ? email[(at + 1)..] : email;

        local = local.Trim();
        domain = domain.Trim();

        if (string.IsNullOrWhiteSpace(local))
        {
            local = "subscriber";
        }

        var tokens = local.Split(new[] { '.', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        string firstName;
        string lastName;

        if (tokens.Length >= 2)
        {
            firstName = tokens[0];
            lastName = string.Join(' ', tokens, 1, tokens.Length - 1);
        }
        else
        {
            firstName = tokens.Length > 0 ? tokens[0] : local;
            lastName = string.IsNullOrWhiteSpace(domain)
                ? "Member"
                : domain.Split('.')[0];
        }

        return (Cap(firstName), Cap(lastName));
    }

    private static string Cap(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        value = value.Trim();
        return value.Length <= 1
            ? value.ToUpperInvariant()
            : char.ToUpperInvariant(value[0]) + value[1..];
    }
}
