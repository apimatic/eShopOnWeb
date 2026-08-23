using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioReferenceFactory
{
    private const string Namespace = "microsoft-eshoponweb-maxio-v1";
    private readonly string _integrationScope;

    public MaxioReferenceFactory(MaxioOptions options)
    {
        var site = string.IsNullOrWhiteSpace(options.BaseUrl) ? options.Subdomain : options.BaseUrl;
        _integrationScope = Hash($"{Namespace}|scope|{site}|{options.ProductFamilyHandle}");
    }

    public string IntegrationScope => _integrationScope;

    public string Customer(string userId) =>
        $"eshop-customer-v1-{Hash($"{Namespace}|{_integrationScope}|user|{userId}")}";

    public string Subscription(string userId, string productHandle) =>
        $"eshop-sub-v1-{Hash($"{Namespace}|{_integrationScope}|user|{userId}|product|{productHandle}")}";

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
