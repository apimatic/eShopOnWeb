using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public Uri GetBaseAddress()
    {
        var baseAddress = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return uri;
    }

    public bool IsValid() =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle) &&
        CanBuildBaseAddress();

    private bool CanBuildBaseAddress()
    {
        try
        {
            _ = GetBaseAddress();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
