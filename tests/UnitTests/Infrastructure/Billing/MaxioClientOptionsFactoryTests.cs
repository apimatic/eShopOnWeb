using System.Net;
using System.Net.Http;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Servers;
using Microsoft.eShopWeb.Infrastructure.Billing.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Billing;

public class MaxioClientOptionsFactoryTests
{
    [Fact]
    public void DerivesTheAddressFromTheConfiguredSubdomain()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "cp-exp-2", ProductFamilyHandle = "f" };

        var address = CapturedAddressFor(settings);

        Assert.Equal("https://cp-exp-2.chargify.com/site.json", address);
    }

    [Fact]
    public void UsesAConfiguredBaseUrlVerbatim()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-2",
            BaseUrl = "https://maxio.internal.example.com/gateway",
            ProductFamilyHandle = "f"
        };

        var address = CapturedAddressFor(settings);

        // The override wins outright: no subdomain is substituted into it.
        Assert.Equal("https://maxio.internal.example.com/gateway/site.json", address);
    }

    [Fact]
    public void SendsTheApiKeyAsBasicAuth()
    {
        var settings = new MaxioSettings { ApiKey = "the-api-key", Subdomain = "cp-exp-2", ProductFamilyHandle = "f" };
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.OK, "{}"));

        Call(settings, handler);

        var expected = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("the-api-key:x"));
        Assert.Equal(expected, _lastAuthorization?.Parameter);
        Assert.Equal("Basic", _lastAuthorization?.Scheme);
    }

    [Theory]
    [InlineData(null, "https://cp-exp-2.chargify.com/site.json")]
    [InlineData("US", "https://cp-exp-2.chargify.com/site.json")]
    [InlineData("eu", "https://cp-exp-2.ebilling.maxio.com/site.json")]
    // Maxio models only two hosting regions, so a value it cannot express — such as an environment name —
    // must fall back rather than fail.
    [InlineData("sandbox", "https://cp-exp-2.chargify.com/site.json")]
    public void ResolvesTheHostingRegionFromConfiguration(string? environment, string expected)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-2",
            ProductFamilyHandle = "f",
            Environment = environment
        };

        Assert.Equal(expected, CapturedAddressFor(settings));
    }

    [Fact]
    public void WritesTheBaseUrlOverrideToTheRegionThatIsActuallyRead()
    {
        var settings = new MaxioSettings
        {
            ApiKey = "k",
            Subdomain = "cp-exp-2",
            BaseUrl = "https://eu-gateway.example.com",
            ProductFamilyHandle = "f",
            Environment = "EU"
        };

        Assert.Equal("https://eu-gateway.example.com/site.json", CapturedAddressFor(settings));
    }

    [Fact]
    public void KeepsTheRetryPipelineAboveTheFloorThePolicyEngineRequires()
    {
        var settings = new MaxioSettings { ApiKey = "k", Subdomain = "s", ProductFamilyHandle = "f", MaxRetries = 0 };

        // Constructing the client is what validates the pipeline; zero attempts would throw here.
        var options = MaxioClientOptionsFactory.Create(settings);
        var exception = Record.Exception(() => new MaxioAdvancedBillingClient(new HttpClient(), options));

        Assert.Null(exception);
        Assert.Equal(1, options.Retry.MaxRetries);
    }

    [Fact]
    public void DefaultsToTheUnitedStatesRegion()
    {
        var options = MaxioClientOptionsFactory.Create(new MaxioSettings { ApiKey = "k", Subdomain = "s" });

        Assert.Equal(ServerEnvironment.Us, options.Environment);
    }

    private System.Net.Http.Headers.AuthenticationHeaderValue? _lastAuthorization;

    private string CapturedAddressFor(MaxioSettings settings)
    {
        var handler = new MaxioStubHandler(_ => MaxioStubHandler.Json(HttpStatusCode.OK, "{}"));
        Call(settings, handler);
        return _capturedAddress!;
    }

    private string? _capturedAddress;

    private void Call(MaxioSettings settings, MaxioStubHandler handler)
    {
        var recorder = new RecordingHandler(this) { InnerHandler = handler };
        var client = new MaxioAdvancedBillingClient(new HttpClient(recorder), MaxioClientOptionsFactory.Create(settings));

        // The body is deliberately unparseable for ReadSite, so this throws — the request is what matters.
        try
        {
            client.Sites.ReadSite().GetAwaiter().GetResult();
        }
        catch
        {
            // Ignored: the assertion is on the outgoing request, not the response.
        }
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        private readonly MaxioClientOptionsFactoryTests _test;

        public RecordingHandler(MaxioClientOptionsFactoryTests test) => _test = test;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _test._capturedAddress = request.RequestUri?.GetLeftPart(UriPartial.Path);
            _test._lastAuthorization = request.Headers.Authorization;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
