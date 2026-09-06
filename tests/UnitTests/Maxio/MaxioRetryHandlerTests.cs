using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

public class MaxioRetryHandlerTests
{
    private static HttpClient CreateClient(RecordingHandler inner, MaxioOptions? options = null)
    {
        var retryHandler = new MaxioRetryHandler(
            new TestOptionsMonitor<MaxioOptions>(options ?? TestOptions.Valid()),
            NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = inner
        };

        return new HttpClient(retryHandler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    [Fact]
    public async Task ServerErrorsAreRetriedForReads()
    {
        var inner = new RecordingHandler()
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.OK, "[]");

        var response = await CreateClient(inner).GetAsync("https://acme.chargify.com/products.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task ServerErrorsAreNotRetriedForWrites()
    {
        var inner = new RecordingHandler()
            .Enqueue(HttpStatusCode.InternalServerError)
            .Enqueue(HttpStatusCode.OK, "{}");

        var response = await CreateClient(inner).PostAsync("https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task ThrottlingIsRetriedEvenForWrites()
    {
        var inner = new RecordingHandler()
            .Enqueue(HttpStatusCode.TooManyRequests)
            .Enqueue(HttpStatusCode.Created, "{}");

        var response = await CreateClient(inner).PostAsync("https://acme.chargify.com/subscriptions.json", new StringContent("{}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task RetriesAreBounded()
    {
        var inner = new RecordingHandler();
        for (var i = 0; i < 10; i++)
        {
            inner.Enqueue(HttpStatusCode.ServiceUnavailable);
        }

        var options = TestOptions.Valid(o => o.MaxRetryAttempts = 2);
        var response = await CreateClient(inner, options).GetAsync("https://acme.chargify.com/products.json");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task ClientErrorsAreNotRetried()
    {
        var inner = new RecordingHandler()
            .Enqueue(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""")
            .Enqueue(HttpStatusCode.OK, "{}");

        var response = await CreateClient(inner).GetAsync("https://acme.chargify.com/customers/lookup.json?reference=x");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(inner.Requests);
    }
}
