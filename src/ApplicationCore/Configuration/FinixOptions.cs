namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class FinixOptions
{
    public const string SectionName = "Finix";

    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? MerchantId { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(MerchantId) &&
        !string.IsNullOrWhiteSpace(Currency);
}
