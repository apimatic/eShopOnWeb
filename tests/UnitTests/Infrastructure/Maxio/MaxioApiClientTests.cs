using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    [Fact]
    public async Task LookingUpAnUnknownCustomerReturnsNullRatherThanThrowing()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.NotFound, string.Empty));

        Assert.Null(await CreateClient(handler).FindCustomerByReferenceAsync("eshoponweb-nobody@example.com"));
    }

    [Fact]
    public async Task TheCustomerReferenceIsUrlEncodedIntoTheLookupQuery()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, """{"customer":{"id":1}}"""));

        await CreateClient(handler).FindCustomerByReferenceAsync("eshoponweb-demo user@microsoft.com");

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("%40", query);
        Assert.DoesNotContain(" ", query);
    }

    [Fact]
    public async Task ProductsAreAddressedByFamilyHandleAndPagedUntilExhausted()
    {
        var page = "[" + string.Join(",", Enumerable.Range(0, 200).Select(index =>
            "{\"product\":{\"id\":" + index + ",\"handle\":\"plan-" + index + "\"}}")) + "]";

        var handler = new StubHandler(request =>
            request.RequestUri!.Query.Contains("page=1")
                ? Response(HttpStatusCode.OK, page)
                : Response(HttpStatusCode.OK, """[{"product":{"id":999,"handle":"last"}}]"""));

        var products = await CreateClient(handler).ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("product_families/handle", handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Contains("eshop-subscribe/products.json", handler.Requests[0].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task PaginationStopsOnAShortPage()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, """[{"product":{"id":1,"handle":"only"}}]"""));

        var products = await CreateClient(handler).ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Single(products);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ANonSuccessResponseCarriesTheStatusAndTheProviderErrors()
    {
        var handler = new StubHandler(_ => Response(
            HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exception.StatusCode);
        Assert.Equal(new[] { "No payment method was on file for the $299.00 balance" }, exception.Errors);
    }

    [Fact]
    public async Task ADuplicateSubmissionIsSurfacedAsSuch()
    {
        var handler = new StubHandler(_ => Response(
            HttpStatusCode.Conflict,
            """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest()));

        Assert.True(exception.IsDuplicateSubmission);
    }

    [Fact]
    public async Task CreatingASubscriptionSendsTheUniquenessTokenAlongsideTheSubscription()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.Created, """{"subscription":{"id":7,"state":"active"}}"""));

        await CreateClient(handler).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscription { ProductHandle = "eshop-pro", CustomerId = 42 },
            UniquenessToken = "token-123"
        });

        var body = handler.Bodies.Single();
        Assert.Contains("\"uniqueness_token\":\"token-123\"", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"customer_id\":42", body);
        // Optional members are omitted rather than sent as null, which Maxio would treat as a value.
        Assert.DoesNotContain("\"reference\"", body);
    }

    [Fact]
    public async Task AThrottledRequestIsRetriedAndThenSucceeds()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
            ++attempts < 3
                ? Response(HttpStatusCode.TooManyRequests, """{"errors":["Your request was denied due to a usage violation."]}""")
                : Response(HttpStatusCode.OK, """{"customer":{"id":1}}"""));

        var customer = await CreateClient(handler, retries: 3).FindCustomerByReferenceAsync("eshoponweb-demo@example.com");

        Assert.Equal(1, customer!.Id);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task RetriesAreBoundedAndTheLastResponseIsSurfaced()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return Response(HttpStatusCode.ServiceUnavailable, string.Empty);
        });

        await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler, retries: 2).FindCustomerByReferenceAsync("eshoponweb-demo@example.com"));

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task AClientErrorIsNotRetried()
    {
        var attempts = 0;
        var handler = new StubHandler(_ =>
        {
            attempts++;
            return Response(HttpStatusCode.UnprocessableEntity, """{"errors":["nope"]}""");
        });

        await Assert.ThrowsAsync<MaxioApiException>(() =>
            CreateClient(handler, retries: 3).CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest()));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AConnectionFailureThatOutlivesTheRetriesBecomesATransportFailure()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<MaxioTransportException>(() =>
            CreateClient(handler, retries: 1).GetSiteAsync());
    }

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static MaxioApiClient CreateClient(StubHandler handler, int retries = 0)
    {
        var settings = new MaxioSettings
        {
            ApiKey = "test-key",
            Subdomain = "acme",
            ProductFamilyHandle = "eshop-subscribe",
            MaxRetryAttempts = retries,
            RequestTimeoutSeconds = 5
        };

        var retryHandler = new MaxioRetryHandler(
            new TestOptionsMonitor<MaxioSettings>(settings),
            NullLogger<MaxioRetryHandler>.Instance)
        {
            InnerHandler = handler
        };

        var httpClient = new HttpClient(retryHandler)
        {
            BaseAddress = settings.ResolveBaseAddress(),
            Timeout = Timeout.InfiniteTimeSpan
        };

        return new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return _respond(request);
        }
    }
}
