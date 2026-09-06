using System.Net;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Client;

public class MaxioRetryHandlerTests
{
    [Fact]
    public async Task Retries_a_read_that_failed_with_a_server_error()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.InternalServerError)
            .Respond(HttpStatusCode.OK, "{}");

        var response = await SendAsync(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Retries_a_read_whose_connection_failed()
    {
        var stub = new StubHttpMessageHandler()
            .Throw<HttpRequestException>()
            .Respond(HttpStatusCode.OK, "{}");

        var response = await SendAsync(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task Never_repeats_a_write_the_provider_may_already_have_applied()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.InternalServerError);

        var response = await SendAsync(stub, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task Repeats_a_write_that_was_only_rate_limited()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.TooManyRequests)
            .Respond(HttpStatusCode.Created, "{}");

        var response = await SendAsync(stub, HttpMethod.Post, body: """{"subscription":{}}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.RequestBodies, body => Assert.Equal("""{"subscription":{}}""", body));
    }

    [Fact]
    public async Task Gives_up_after_the_configured_number_of_attempts()
    {
        var stub = new StubHttpMessageHandler()
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.ServiceUnavailable)
            .Respond(HttpStatusCode.ServiceUnavailable);

        var response = await SendAsync(stub, HttpMethod.Get, maxRetryAttempts: 2);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
    }

    [Fact]
    public async Task Does_not_retry_a_response_the_caller_can_act_on()
    {
        var stub = new StubHttpMessageHandler().Respond(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""");

        var response = await SendAsync(stub, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Single(stub.Requests);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        StubHttpMessageHandler stub,
        HttpMethod method,
        string? body = null,
        int maxRetryAttempts = 3)
    {
        var options = new MaxioOptions
        {
            ApiKey = "k",
            Subdomain = "site",
            ProductFamilyHandle = "family",
            MaxRetryAttempts = maxRetryAttempts,
            RetryBaseDelayMilliseconds = 1
        };

        var handler = new MaxioRetryHandler(
            new StaticOptionsMonitor<MaxioOptions>(options),
            NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = stub
        };

        using var client = new HttpClient(handler) { BaseAddress = options.ResolveBaseAddress() };

        var request = new HttpRequestMessage(method, "site.json");
        if (body is not null)
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        return await client.SendAsync(request);
    }
}
