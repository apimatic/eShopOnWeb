namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>
/// Bound from the <c>Plaid:</c> configuration section. The values come from user-secrets or the environment,
/// never from files in the repository.
/// </summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }

    /// <summary>Optional override. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The Plaid API version header (not a credential).</summary>
    public string Version { get; set; } = "2020-09-14";
}

/// <summary>Shop-side settings of the pay-by-bank flow (not Plaid credentials).</summary>
public class BankPaymentSettings
{
    public const string SectionName = "BankPayment";

    /// <summary>Flat fee the shopper pays for express (same-day) payments.</summary>
    public decimal ExpressFee { get; set; } = 3.00m;

    /// <summary>Largest amount the same-day ACH network carries (NACHA rule).</summary>
    public decimal ExpressMaxAmount { get; set; } = 1_000_000.00m;

    /// <summary>Local (US Eastern) time after which a same-day ACH submission rolls to the next business day.</summary>
    public string SameDayCutoffEastern { get; set; } = "15:30";
}
