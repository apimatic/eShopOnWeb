using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>
/// Builds a real <see cref="MaxioBillingClient"/> over a stubbed transport, so tests exercise the
/// production request-building, deserialisation, mapping and error-translation code paths.
/// </summary>
public static class BillingClientFixture
{
    public const string ProductFamilyHandle = "eshop-subscribe";

    public static MaxioSettings DefaultSettings() => new()
    {
        ApiKey = "test-api-key",
        Subdomain = "test-site",
        Environment = MaxioSettings.UsRegion,
        ProductFamilyHandle = ProductFamilyHandle
    };

    /// <summary>Creates a client whose next responses are the supplied JSON bodies, in order.</summary>
    public static (IBillingClient Client, StubHttpMessageHandler Handler) Create(params string[] jsonResponses)
    {
        return Create(DefaultSettings(), jsonResponses);
    }

    public static (IBillingClient Client, StubHttpMessageHandler Handler) Create(MaxioSettings settings,
        params string[] jsonResponses)
    {
        var handler = new StubHttpMessageHandler();
        foreach (var json in jsonResponses)
        {
            handler.RespondWith(json);
        }

        return (Build(settings, handler), handler);
    }

    /// <summary>Creates a client whose next response is an error with the given status and body.</summary>
    public static (IBillingClient Client, StubHttpMessageHandler Handler) CreateFailing(HttpStatusCode statusCode,
        string body = "{}")
    {
        var handler = new StubHttpMessageHandler();
        handler.RespondWith(body, statusCode);

        return (Build(DefaultSettings(), handler), handler);
    }

    public static IBillingClient Build(MaxioSettings settings, HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(settings.ResolveBaseUrl())
        };

        return new MaxioBillingClient(httpClient,
            Options.Create(settings),
            Substitute.For<IAppLogger<MaxioBillingClient>>());
    }
}
