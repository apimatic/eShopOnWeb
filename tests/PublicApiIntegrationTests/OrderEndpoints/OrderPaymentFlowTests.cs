using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class OrderPaymentFlowTests
{
    private static WebApplicationFactory<Program> _application = null!;

    [ClassInitialize]
    public static void ClassInitialize(TestContext _)
    {
        _application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Singleton: FakePayPalGateway's in-memory capture ledger stands in for
                    // PayPal's own server-side state, which persists across separate HTTP
                    // calls to our app (pay/fulfil/refund are three separate requests).
                    services.AddSingleton<IPayPalGateway, FakePayPalGateway>();
                });
            });
    }

    private static HttpClient ClientFor(string token)
    {
        var client = _application.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<int> CreateOrderAsync(HttpClient client)
    {
        var request = new CreateOrderRequest
        {
            Items = new List<OrderItemRequest> { new() { CatalogItemId = 1, Quantity = 1 } },
            ShipToAddress = new AddressRequest { Street = "1 Main St", City = "Redmond", State = "WA", Country = "USA", ZipCode = "98052" }
        };
        var response = await client.PostAsJsonAsync("api/orders", request);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        return body!.OrderId;
    }

    [TestMethod]
    public async Task CreateOrder_WithoutToken_ReturnsUnauthorized()
    {
        var client = _application.CreateClient();
        var response = await client.PostAsJsonAsync("api/orders", new CreateOrderRequest());
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task PayThenFulfil_CapturesPaymentAndUpdatesMyOrders()
    {
        var buyerToken = ApiTokenHelper.GetUserToken("buyer-pay-fulfil@example.com");
        var buyerClient = ClientFor(buyerToken);

        var orderId = await CreateOrderAsync(buyerClient);

        var payResponse = await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new PayOrderRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123", CardholderName = "Test Buyer" }
        });
        payResponse.EnsureSuccessStatusCode();

        var adminClient = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var fulfilResponse = await adminClient.PostAsync($"api/orders/{orderId}/fulfil", null);
        fulfilResponse.EnsureSuccessStatusCode();
        var fulfilBody = await fulfilResponse.Content.ReadFromJsonAsync<FulfilOrderResponse>();
        Assert.IsTrue(fulfilBody!.CapturedAmount > 0);

        var myOrders = await buyerClient.GetFromJsonAsync<MyOrdersResponse>("api/my-orders");
        var order = myOrders!.Orders.Find(o => o.OrderId == orderId);
        Assert.IsNotNull(order);
        Assert.AreEqual("Fulfilled", order!.Status);
        Assert.IsNotNull(order.CapturedAmount);
    }

    [TestMethod]
    public async Task Fulfil_RejectsNonAdmin()
    {
        var buyerToken = ApiTokenHelper.GetUserToken("buyer-fulfil-reject@example.com");
        var buyerClient = ClientFor(buyerToken);
        var orderId = await CreateOrderAsync(buyerClient);

        var response = await buyerClient.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Cancel_RejectsNonAdmin()
    {
        var buyerClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-cancel-reject@example.com"));
        var orderId = await CreateOrderAsync(buyerClient);

        var response = await buyerClient.PostAsync($"api/orders/{orderId}/cancel", null);
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task Reconciliation_RejectsNonAdmin()
    {
        var buyerClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-reconciliation-reject@example.com"));
        var response = await buyerClient.GetAsync("api/reconciliation?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task CancelBeforeFulfilment_ReleasesHold()
    {
        var buyerClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-cancel-flow@example.com"));
        var orderId = await CreateOrderAsync(buyerClient);

        var payResponse = await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new PayOrderRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" }
        });
        payResponse.EnsureSuccessStatusCode();

        var adminClient = ClientFor(ApiTokenHelper.GetAdminUserToken());
        var cancelResponse = await adminClient.PostAsync($"api/orders/{orderId}/cancel", null);
        cancelResponse.EnsureSuccessStatusCode();
        var cancelBody = await cancelResponse.Content.ReadFromJsonAsync<CancelOrderResponse>();
        Assert.AreEqual("Cancelled", cancelBody!.Status);

        // A cancelled order can no longer be fulfilled.
        var fulfilResponse = await adminClient.PostAsync($"api/orders/{orderId}/fulfil", null);
        Assert.AreEqual(HttpStatusCode.Conflict, fulfilResponse.StatusCode);
    }

    [TestMethod]
    public async Task Refund_IsIdempotentByKey()
    {
        var buyerClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-refund-flow@example.com"));
        var orderId = await CreateOrderAsync(buyerClient);

        (await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new PayOrderRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" }
        })).EnsureSuccessStatusCode();

        var adminClient = ClientFor(ApiTokenHelper.GetAdminUserToken());
        (await adminClient.PostAsync($"api/orders/{orderId}/fulfil", null)).EnsureSuccessStatusCode();

        var idempotencyKey = Guid.NewGuid().ToString();
        var refundRequest = new RefundOrderRequest { IdempotencyKey = idempotencyKey };

        var first = await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<RefundOrderResponse>();

        var second = await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/refunds", refundRequest);
        second.EnsureSuccessStatusCode();
        var secondBody = await second.Content.ReadFromJsonAsync<RefundOrderResponse>();

        Assert.AreEqual(firstBody!.RefundId, secondBody!.RefundId);
    }

    [TestMethod]
    public async Task MyOrders_DoesNotShowOtherBuyersOrders()
    {
        var buyerAClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-a-isolation@example.com"));
        var buyerBClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-b-isolation@example.com"));

        var orderId = await CreateOrderAsync(buyerAClient);

        var buyerBOrders = await buyerBClient.GetFromJsonAsync<MyOrdersResponse>("api/my-orders");
        Assert.IsFalse(buyerBOrders!.Orders.Exists(o => o.OrderId == orderId));

        // Buyer B cannot pay for buyer A's order either.
        var payAsB = await buyerBClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new PayOrderRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123" }
        });
        Assert.AreEqual(HttpStatusCode.NotFound, payAsB.StatusCode);
    }

    [TestMethod]
    public async Task SavedCard_IsScopedToItsOwner()
    {
        var buyerAClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-a-cards@example.com"));
        var buyerBClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-b-cards@example.com"));

        var createResponse = await buyerAClient.PostAsJsonAsync("api/payment-methods", new CreatePaymentMethodRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123", CardholderName = "Buyer A" }
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatePaymentMethodResponse>();

        var buyerBList = await buyerBClient.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods");
        Assert.IsFalse(buyerBList!.PaymentMethods.Exists(m => m.PaymentMethodId == created!.PaymentMethodId));

        var deleteAsB = await buyerBClient.DeleteAsync($"api/payment-methods/{created!.PaymentMethodId}");
        Assert.AreEqual(HttpStatusCode.NotFound, deleteAsB.StatusCode);

        var buyerAList = await buyerAClient.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods");
        Assert.IsTrue(buyerAList!.PaymentMethods.Exists(m => m.PaymentMethodId == created.PaymentMethodId));

        var deleteAsA = await buyerAClient.DeleteAsync($"api/payment-methods/{created.PaymentMethodId}");
        deleteAsA.EnsureSuccessStatusCode();

        buyerAList = await buyerAClient.GetFromJsonAsync<ListPaymentMethodsResponse>("api/payment-methods");
        Assert.IsFalse(buyerAList!.PaymentMethods.Exists(m => m.PaymentMethodId == created.PaymentMethodId));
    }

    [TestMethod]
    public async Task PayWithSavedCard_ReusesVaultedPaymentMethod()
    {
        var buyerClient = ClientFor(ApiTokenHelper.GetUserToken("buyer-saved-card-pay@example.com"));

        var createResponse = await buyerClient.PostAsJsonAsync("api/payment-methods", new CreatePaymentMethodRequest
        {
            Card = new CardDetailsRequest { Number = "4111111111111111", Expiry = "2030-01", SecurityCode = "123", CardholderName = "Buyer" }
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<CreatePaymentMethodResponse>();

        var orderId = await CreateOrderAsync(buyerClient);
        var payResponse = await buyerClient.PostAsJsonAsync($"api/orders/{orderId}/pay", new PayOrderRequest
        {
            SavedPaymentMethodId = created!.PaymentMethodId
        });
        payResponse.EnsureSuccessStatusCode();
        var payBody = await payResponse.Content.ReadFromJsonAsync<PayOrderResponse>();
        Assert.IsFalse(string.IsNullOrEmpty(payBody!.PayPalAuthorizationId));
    }
}
