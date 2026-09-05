using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; init; }
    public string? Subdomain { get; init; }
    public string? ProductFamilyHandle { get; init; }
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Required(Subdomain, nameof(Subdomain))}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }

    public void EnsureConfigured()
    {
        try
        {
            _ = Required(ApiKey, nameof(ApiKey));
            _ = Required(ProductFamilyHandle, nameof(ProductFamilyHandle));
            _ = GetBaseUri();
        }
        catch (InvalidOperationException exception)
        {
            throw new MaxioConfigurationException(exception.Message, exception);
        }
    }

    public static string Required(string? value, string name) => !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException($"Maxio:{name} is required.");
}

public sealed class MaxioConfigurationException : InvalidOperationException
{
    public MaxioConfigurationException(string message, Exception innerException) : base(message, innerException) { }
}
