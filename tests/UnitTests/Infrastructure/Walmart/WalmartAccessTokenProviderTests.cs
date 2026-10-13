using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Walmart;

public class WalmartAccessTokenProviderTests
{
    private static WalmartAccessTokenProvider Create(StubHttpMessageHandler handler, WalmartSettings settings) =>
        new(new SingleClientFactory(handler), Options.Create(settings), NullLogger<WalmartAccessTokenProvider>.Instance);

    private static HttpResponseMessage TokenOk() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"access_token\":\"abc123\",\"token_type\":\"Bearer\",\"expires_in\":900}",
                System.Text.Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task RequestsClientCredentialsTokenWithOverrideAppliedToAuthServer()
    {
        var handler = new StubHttpMessageHandler(_ => TokenOk());
        var provider = Create(handler, new WalmartSettings
        {
            ClientId = "the-id",
            ClientSecret = "the-secret",
            BaseUrl = "http://localhost:9000"
        });

        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("abc123", token);
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:9000/seller/auth/v4/token", request.RequestUri!.ToString());

        var body = handler.RequestBodies.Single()!;
        Assert.Contains("grant_type=client_credentials", body);
        Assert.Contains("client_id=the-id", body);
        Assert.Contains("client_secret=the-secret", body);
    }

    [Fact]
    public async Task CachesTokenAcrossCalls()
    {
        var handler = new StubHttpMessageHandler(_ => TokenOk());
        var provider = Create(handler, new WalmartSettings { ClientId = "id", ClientSecret = "secret" });

        var first = await provider.GetAccessTokenAsync();
        var second = await provider.GetAccessTokenAsync();

        Assert.Equal(first, second);
        Assert.Single(handler.Requests); // only one network call
    }

    [Fact]
    public async Task MissingCredentialsThrowsWithoutCallingNetwork()
    {
        var handler = new StubHttpMessageHandler(_ => TokenOk());
        var provider = Create(handler, new WalmartSettings { ClientId = null, ClientSecret = null });

        await Assert.ThrowsAsync<WalmartApiException>(() => provider.GetAccessTokenAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AuthFailureThrowsWalmartApiExceptionWithoutLeakingSecret()
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid_client\"}", System.Text.Encoding.UTF8, "application/json")
        });
        var provider = Create(handler, new WalmartSettings { ClientId = "id", ClientSecret = "topsecret" });

        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => provider.GetAccessTokenAsync());
        Assert.DoesNotContain("topsecret", ex.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }
}
