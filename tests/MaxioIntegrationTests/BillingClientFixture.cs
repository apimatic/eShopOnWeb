using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> wired to a stub handler. Tests point the client at a
/// local URL, which is also the proof that <c>Maxio:BaseUrl</c> is honoured — if the client ignored
/// the override it would try to reach a real Maxio host and every test here would fail.
/// </summary>
public static class BillingClientFixture
{
    public const string StubBaseUrl = "http://localhost:8080";
    public const string ApiKey = "test-api-key";
    public const string FamilyHandle = "eshop-subscribe";
    public const string ComponentHandle = "api-call";

    public static MaxioSettings Settings() => new()
    {
        ApiKey = ApiKey,
        Subdomain = "cp-exp-4",
        Environment = "US",
        BaseUrl = StubBaseUrl,
        ProductFamilyHandle = FamilyHandle,
        DefaultProductHandle = "eshop-pro",
        AlternateProductHandle = "basic-plan",
        MeteredComponentHandle = ComponentHandle,
        PaymentCollectionMethod = "remittance"
    };

    public static MaxioBillingClient Create(StubHttpMessageHandler handler, MaxioSettings? settings = null) =>
        new(new HttpClient(handler), Options.Create(settings ?? Settings()));
}
