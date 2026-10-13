using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Walmart;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Walmart;

public class WalmartTokenProviderTests
{
    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }

    private static WalmartTokenProvider Create(StubHttpMessageHandler handler, string? baseUrl = "http://localhost:39070")
    {
        var options = Options.Create(new WalmartSettings { ClientId = "the-id", ClientSecret = "the-secret", BaseUrl = baseUrl });
        return new WalmartTokenProvider(new SingleClientFactory(handler), options, NullLogger<WalmartTokenProvider>.Instance);
    }

    [Fact]
    public async Task PostsClientCredentialsFormAndReturnsAccessToken()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"abc123\",\"token_type\":\"Bearer\",\"expires_in\":900}"));

        var provider = Create(handler);
        var token = await provider.GetAccessTokenAsync();

        Assert.Equal("abc123", token);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost:39070/seller/auth/v4/token", request.Uri.ToString());
        Assert.Equal("application/x-www-form-urlencoded", request.ContentType);
        Assert.Contains("grant_type=client_credentials", request.Body);
        Assert.Contains("client_id=the-id", request.Body);
        Assert.Contains("client_secret=the-secret", request.Body);
    }

    [Fact]
    public async Task CachesTokenAcrossCalls()
    {
        var calls = 0;
        var handler = new StubHttpMessageHandler((req, body) =>
        {
            calls++;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, "{\"access_token\":\"abc123\",\"token_type\":\"Bearer\",\"expires_in\":900}");
        });

        var provider = Create(handler);
        await provider.GetAccessTokenAsync();
        await provider.GetAccessTokenAsync();

        Assert.Equal(1, calls); // second call served from cache
    }

    [Fact]
    public async Task ThrowsOnInvalidClient()
    {
        var handler = new StubHttpMessageHandler((req, body) =>
            StubHttpMessageHandler.Json(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\",\"error_description\":\"bad creds\"}"));

        var provider = Create(handler);
        var ex = await Assert.ThrowsAsync<WalmartApiException>(() => provider.GetAccessTokenAsync());
        Assert.Contains("invalid_client", ex.Message);
    }
}
