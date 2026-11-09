namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>The <c>Stax:</c> configuration section. Credentials come from user-secrets / environment, never from source.</summary>
public class StaxSettings
{
    public const string SECTION = "Stax";

    public string? ApiKey { get; set; }
    public string? PlatformApiKey { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>Shop-side payment policy (not secret).</summary>
public class PaymentsSettings
{
    public const string SECTION = "Payments";

    /// <summary>Card processing fee passed on to shoppers, as a percentage of the order amount.</summary>
    public decimal CardFeePercent { get; set; } = 3.0m;

    /// <summary>How often pending bank settlements and refunds are re-checked against Stax.</summary>
    public int SyncIntervalSeconds { get; set; } = 300;
}
