namespace Microsoft.eShopWeb.PublicApi.PayByBank;

/// <summary>The <c>Plaid:</c> configuration section.</summary>
public class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }

    /// <summary><c>sandbox</c> or <c>production</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Shop-side pay-by-bank settings (the <c>PayByBank:</c> section).</summary>
public class PayByBankOptions
{
    public const string SectionName = "PayByBank";

    /// <summary>Flat fee the shopper pays for express (same-day) payment.</summary>
    public decimal ExpressFee { get; set; } = 5.00m;
}
