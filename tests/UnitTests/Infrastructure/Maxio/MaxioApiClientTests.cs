using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioApiClientTests
{
    private static (MaxioApiClient Client, StubHandler Handler) CreateClient(params StubResponse[] responses)
    {
        var handler = new StubHandler(responses);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://acme.chargify.com/") };

        return (new MaxioApiClient(httpClient, NullLogger<MaxioApiClient>.Instance), handler);
    }

    private static StubResponse Ok(string json) => new(HttpStatusCode.OK, json);

    [Fact]
    public async Task ListProductsForFamilyAsync_AddressesTheFamilyByHandle()
    {
        var (client, handler) = CreateClient(Ok("[]"));

        await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal("https://acme.chargify.com/product_families/handle:eshop-subscribe/products.json?page=1&per_page=200",
            handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task ListProductsForFamilyAsync_UnwrapsTheEnvelopes()
    {
        var (client, _) = CreateClient(Ok("""
            [{"product":{"id":7,"handle":"eshop-pro","name":"Pro Plan","price_in_cents":29900,"interval":1,"interval_unit":"month","require_credit_card":false,"archived_at":null}}]
            """));

        var product = Assert.Single(await client.ListProductsForFamilyAsync("eshop-subscribe"));

        Assert.Equal("eshop-pro", product.Handle);
        Assert.Equal(29900, product.PriceInCents);
        Assert.Null(product.ArchivedAt);
    }

    [Fact]
    public async Task ListProductsForFamilyAsync_FollowsPagination()
    {
        var fullPage = "[" + string.Join(",", Enumerable.Range(0, 200)
            .Select(index => "{\"product\":{\"id\":" + index + ",\"handle\":\"plan-" + index + "\"}}")) + "]";

        var (client, handler) = CreateClient(Ok(fullPage), Ok("""[{"product":{"id":999,"handle":"last-plan"}}]"""));

        var products = await client.ListProductsForFamilyAsync("eshop-subscribe");

        Assert.Equal(201, products.Count);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("page=2", handler.Requests[1].RequestUri!.Query);
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_EscapesTheReferenceIntoTheQueryString()
    {
        var (client, handler) = CreateClient(Ok("""{"customer":{"id":1,"reference":"eshoponweb:a@b.com"}}"""));

        var customer = await client.FindCustomerByReferenceAsync("eshoponweb:a@b.com");

        Assert.Equal(1, customer!.Id);
        Assert.Equal("https://acme.chargify.com/customers/lookup.json?reference=eshoponweb%3Aa%40b.com",
            handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task FindCustomerByReferenceAsync_ReturnsNullWhenTheCustomerDoesNotExist()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.NotFound, string.Empty));

        Assert.Null(await client.FindCustomerByReferenceAsync("eshoponweb:nobody@example.com"));
    }

    [Fact]
    public async Task CreateCustomerAsync_PostsTheCustomerAsJsonWithoutNullMembers()
    {
        var (client, handler) = CreateClient(new StubResponse(HttpStatusCode.Created, """{"customer":{"id":42}}"""));

        var customer = await client.CreateCustomerAsync(new MaxioCustomerAttributes
        {
            FirstName = "Demo",
            LastName = "User",
            Email = "demouser@microsoft.com",
            Reference = "eshoponweb:demouser@microsoft.com"
        });

        Assert.Equal(42, customer.Id);
        Assert.Equal("application/json", handler.Requests.Single().Content!.Headers.ContentType!.MediaType);
        Assert.Equal("""{"customer":{"first_name":"Demo","last_name":"User","email":"demouser@microsoft.com","reference":"eshoponweb:demouser@microsoft.com"}}""",
            handler.RequestBodies.Single());
    }

    [Fact]
    public async Task CreateSubscriptionAsync_SendsTheUniquenessTokenAlongsideTheSubscription()
    {
        var (client, handler) = CreateClient(new StubResponse(HttpStatusCode.Created, """{"subscription":{"id":9,"state":"active"}}"""));

        await client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest
        {
            Subscription = new MaxioSubscriptionAttributes
            {
                ProductHandle = "eshop-pro",
                CustomerId = 42,
                PaymentCollectionMethod = "remittance",
                Reference = "eshoponweb:demouser@microsoft.com:eshop-pro:0"
            },
            UniquenessToken = "aa5e5b8f-0000-0000-0000-000000000000"
        });

        var body = handler.RequestBodies.Single();
        Assert.Contains("\"uniqueness_token\":\"aa5e5b8f-0000-0000-0000-000000000000\"", body);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", body);
        Assert.Contains("\"payment_collection_method\":\"remittance\"", body);
    }

    [Fact]
    public async Task SendAsync_MapsAConflictToADuplicateSubmission()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.Conflict, """{"errors":["DuplicatePrevention::DuplicateSubmissionError"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioDuplicateSubmissionException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.Equal("DuplicatePrevention::DuplicateSubmissionError", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task SendAsync_MapsAnUnprocessableEntityToAValidationFailure()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.UnprocessableEntity,
            """{"errors":["Reference: must be unique - that value has been taken."]}"""));

        var exception = await Assert.ThrowsAsync<MaxioValidationException>(
            () => client.CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.True(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task SendAsync_RecognisesAValidationFailureThatIsNotADuplicate()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.UnprocessableEntity,
            """{"errors":["No payment method was on file for the $299.00 balance"]}"""));

        var exception = await Assert.ThrowsAsync<MaxioValidationException>(
            () => client.CreateSubscriptionAsync(new MaxioCreateSubscriptionRequest()));

        Assert.False(exception.IsDuplicateReference);
    }

    [Fact]
    public async Task SendAsync_ReadsErrorsGivenAsAnObject()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.UnprocessableEntity, """{"errors":{"customer":"is invalid"}}"""));

        var exception = await Assert.ThrowsAsync<MaxioValidationException>(
            () => client.CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.Equal("customer: is invalid", Assert.Single(exception.Errors));
    }

    [Fact]
    public async Task SendAsync_SurvivesAnErrorBodyThatIsNotJson()
    {
        var (client, _) = CreateClient(new StubResponse(HttpStatusCode.BadGateway, "<html>502 Bad Gateway</html>"));

        var exception = await Assert.ThrowsAsync<MaxioApiException>(
            () => client.CreateCustomerAsync(new MaxioCustomerAttributes()));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Empty(exception.Errors);
    }

    [Fact]
    public async Task ReadSiteAsync_ReportsTheSiteArchitecture()
    {
        var (client, _) = CreateClient(Ok("""{"site":{"id":1,"subdomain":"acme","currency":"USD","relationship_invoicing_enabled":true,"test":true}}"""));

        var site = await client.ReadSiteAsync();

        Assert.True(site.RelationshipInvoicingEnabled);
        Assert.Equal("USD", site.Currency);
    }

    private sealed record StubResponse(HttpStatusCode StatusCode, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<StubResponse> _responses;

        public StubHandler(IEnumerable<StubResponse> responses) => _responses = new Queue<StubResponse>(responses);

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> RequestBodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            var response = _responses.Count > 0 ? _responses.Dequeue() : new StubResponse(HttpStatusCode.OK, "[]");

            return new HttpResponseMessage(response.StatusCode)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            };
        }
    }
}
