using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

/// <summary>
/// Exercises MaxioApiClient against spec-shaped JSON fixtures
/// (maxio-spec/openapi.yaml response envelopes: {"product": {...}},
/// {"subscription": {...}}, {"customer": {...}}, snake_case fields,
/// "errors" as array or map).
/// </summary>
public class MaxioApiClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastPath { get; private set; }
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastPath = request.RequestUri!.PathAndQuery;
            return Task.FromResult(_responder(request));
        }
    }

    private const string JsonContentType = "application/json";

    private static HttpResponseMessage Ok(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, JsonContentType)
        };

    private static HttpResponseMessage Error(HttpStatusCode status, object body) =>
        new(status)
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, JsonContentType)
        };

    private static (MaxioApiClient Client, FakeHandler Handler) CreateClient(FakeHandler handler)
    {
        var options = Options.Create(new MaxioOptions
        {
            ApiKey = "test-api-key",
            Subdomain = "cp-exp-3",
            Environment = "US",
            ProductFamilyHandle = "eshop-subscribe"
        });
        var client = new MaxioApiClient(new HttpClient(handler), options);
        return (client, handler);
    }

    private static object ProductResponse(int id, string handle, string familyHandle) => new
    {
        product = new
        {
            id,
            handle,
            name = "Pro Plan",
            description = "The pro plan",
            price_in_cents = 29900,
            interval = 1,
            interval_unit = "month",
            archived_at = (DateTime?)null,
            require_credit_card = false,
            product_family = new { id = 3023074, handle = familyHandle, name = "eShop Subscribe" }
        }
    };

    [Fact]
    public async Task GetProductByHandleAsync_ParsesSpecEnvelopeAndSnakeCase()
    {
        var handler = new FakeHandler(_ => Ok(ProductResponse(7130997, "eshop-pro", "eshop-subscribe")));
        var (client, _) = CreateClient(handler);

        var product = await client.GetProductByHandleAsync("eshop-pro");

        Assert.NotNull(product);
        Assert.Equal(7130997, product!.Id);
        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Equal("month", product.IntervalUnit);
        Assert.Equal("eshop-subscribe", product.ProductFamily!.Handle);
        Assert.False(product.RequireCreditCard);
        Assert.Null(product.ArchivedAt);
        Assert.EndsWith("/products/handle/eshop-pro.json", handler.LastPath);
    }

    [Fact]
    public async Task GetProductByHandleAsync_ReturnsNullOn404()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var (client, _) = CreateClient(handler);

        var product = await client.GetProductByHandleAsync("nope");

        Assert.Null(product);
    }

    [Fact]
    public async Task GetCustomerByReferenceAsync_SendsBasicAuthAndQuery()
    {
        var handler = new FakeHandler(_ => Ok(new
        {
            customer = new { id = 1, first_name = "demouser", last_name = "Customer", email = "a@b.com", reference = "u-1" }
        }));
        var (client, _) = CreateClient(handler);

        var customer = await client.GetCustomerByReferenceAsync("u-1");

        Assert.NotNull(customer);
        Assert.Equal(1, customer!.Id);
        Assert.Equal("demouser", customer.FirstName);
        Assert.Equal("u-1", customer.Reference);
        var auth = handler.LastRequest!.Headers.Authorization!;
        Assert.Equal("Basic", auth.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes("test-api-key:x")), auth.Parameter);
        Assert.EndsWith("/customers/lookup.json?reference=u-1", handler.LastPath);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_PostsSpecRequestEnvelope()
    {
        string? requestBody = null;
        var handler = new FakeHandler(request =>
        {
            requestBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Ok(new
            {
                subscription = new
                {
                    id = 42,
                    state = "active",
                    product_price_in_cents = 29900,
                    reference = "eshop-u1-eshop-pro",
                    current_period_ends_at = "2026-11-05T14:48:10-05:00",
                    activated_at = "2026-10-05T14:48:12-05:00",
                    created_at = "2026-10-05T14:48:10-05:00",
                    customer = new { id = 7, reference = "u1" },
                    product = new { id = 3, handle = "eshop-pro", name = "Pro Plan", price_in_cents = 29900 }
                }
            });
        });
        var (client, _) = CreateClient(handler);

        var subscription = await client.CreateSubscriptionAsync(new CreateMaxioSubscriptionRequest
        {
            Subscription = new CreateMaxioSubscriptionPayload
            {
                ProductHandle = "eshop-pro",
                CustomerId = 7,
                Reference = "eshop-u1-eshop-pro"
            }
        });

        Assert.NotNull(requestBody);
        Assert.Contains("\"subscription\":", requestBody);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", requestBody);
        Assert.Contains("\"customer_id\":7", requestBody);
        Assert.Contains("\"reference\":\"eshop-u1-eshop-pro\"", requestBody);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", requestBody);

        Assert.Equal(42, subscription.Id);
        Assert.Equal("active", subscription.State);
        Assert.Equal(29900, subscription.ProductPriceInCents);
        Assert.Equal("eshop-u1-eshop-pro", subscription.Reference);
        Assert.NotNull(subscription.CurrentPeriodEndsAt);
        Assert.Equal(7, subscription.Customer!.Id);
        Assert.Equal("eshop-pro", subscription.Product!.Handle);
        Assert.EndsWith("/subscriptions.json", handler.LastPath);
    }

    [Fact]
    public async Task ListCustomerSubscriptionsAsync_ParsesArrayOfWrappers()
    {
        var handler = new FakeHandler(_ => Ok(new object[]
        {
            new { subscription = new { id = 1, state = "active", product_price_in_cents = 29900 } },
            new { subscription = new { id = 2, state = "canceled", product_price_in_cents = 2900 } }
        }));
        var (client, _) = CreateClient(handler);

        var subscriptions = await client.ListCustomerSubscriptionsAsync(7);

        Assert.Equal(2, subscriptions.Count);
        Assert.Equal("active", subscriptions[0].State);
        Assert.Equal("canceled", subscriptions[1].State);
        Assert.EndsWith("/customers/7/subscriptions.json", handler.LastPath);
    }

    [Fact]
    public async Task ErrorResponses_SurfaceSpecErrorModels()
    {
        var handler = new FakeHandler(_ => Error(HttpStatusCode.UnprocessableEntity,
            new { errors = new[] { "Plan: is invalid" } }));
        var (client, _) = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => client.CreateSubscriptionAsync(
            new CreateMaxioSubscriptionRequest()));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("Plan: is invalid", ex.Message);
    }

    [Fact]
    public async Task ErrorResponse_WithMap_ShowsFieldAndMessage()
    {
        var handler = new FakeHandler(_ => Error(HttpStatusCode.UnprocessableEntity,
            new { errors = new { customer = "can't be blank" } }));
        var (client, _) = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<MaxioApiException>(() => client.CreateCustomerAsync(
            new CreateMaxioCustomerRequest()));

        Assert.Equal(422, ex.StatusCode);
        Assert.Contains("customer: can't be blank", ex.Message);
    }

    [Fact]
    public async Task ListProductsAsync_PaginatesUntilExhausted()
    {
        var requestedPages = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var query = request.RequestUri!.Query;
            requestedPages.Add(query);
            var page = int.Parse(query.Split("page=")[1].Split('&')[0]);
            if (page == 1)
            {
                return Ok(Enumerable.Range(1, 100).Select(i => new
                {
                    product = new { id = i, handle = $"plan-{i}", name = $"Plan {i}", price_in_cents = 1000 + i }
                }).ToArray());
            }
            return Ok(Array.Empty<object>());
        });
        var (client, _) = CreateClient(handler);

        var products = await client.ListProductsAsync();

        Assert.Equal(100, products.Count);
        Assert.Equal(2, requestedPages.Count);
        Assert.Contains("per_page=100", requestedPages[0]);
    }
}