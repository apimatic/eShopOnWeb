using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Payments;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Payments;

[TestClass]
public class CommerceServiceTests
{
    [TestMethod]
    public async Task PlacesOrderAtCatalogPriceAndAuthorizesOnlyOnce()
    {
        await using var context = Context();
        context.CatalogItems.Add(new CatalogItem(1, 1, "item", "Item", 8.50m, "item.png"));
        await context.SaveChangesAsync();
        var gateway = new StubPayPalGateway();
        var service = Service(context, gateway);
        var order = await service.PlaceOrderAsync("shopper@example.com", Request(), default);

        Assert.AreEqual(8.50m, order.Total);
        Assert.AreEqual("AwaitingPayment", order.PaymentState);

        var paymentRequest = new PayOrderRequest(Card(), null);
        var first = await service.PayAsync("shopper@example.com", order.OrderId, paymentRequest, default);
        var second = await service.PayAsync("shopper@example.com", order.OrderId, paymentRequest, default);

        Assert.AreEqual("Authorized", first.PaymentState);
        Assert.AreEqual(first.AuthorizationId, second.AuthorizationId);
        Assert.AreEqual(1, gateway.AuthorizeCalls);
        Assert.IsTrue(gateway.LastExternalReference?.StartsWith("eshop-", StringComparison.Ordinal));
        Assert.AreEqual((await context.Payments.SingleAsync()).ExternalReference,
            gateway.LastExternalReference);
    }

    [TestMethod]
    public async Task SavedCardCannotBeUsedByAnotherShopper()
    {
        await using var context = Context();
        context.CatalogItems.Add(new CatalogItem(1, 1, "item", "Item", 8.50m, "item.png"));
        await context.SaveChangesAsync();
        var service = Service(context, new StubPayPalGateway());
        var saved = await service.SavePaymentMethodAsync("owner@example.com",
            new SavePaymentMethodRequest(Card()), default);
        var order = await service.PlaceOrderAsync("other@example.com", Request(), default);

        var ex = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.PayAsync("other@example.com", order.OrderId,
                new PayOrderRequest(null, saved.PaymentMethodId), default));

        Assert.AreEqual(404, ex.StatusCode);
    }

    [TestMethod]
    [DataRow(-32, 0)]
    [DataRow(-1, 1)]
    public async Task ReconciliationRejectsProviderUnsupportedDateRanges(int fromDays, int toDays)
    {
        await using var context = Context();
        var gateway = new StubPayPalGateway();
        var service = Service(context, gateway);
        var now = DateTimeOffset.UtcNow;

        var ex = await Assert.ThrowsExceptionAsync<PaymentApiException>(() =>
            service.ReconcileAsync(now.AddDays(fromDays), now.AddDays(toDays), default));

        Assert.AreEqual(422, ex.StatusCode);
        Assert.AreEqual("invalid_date_range", ex.Code);
        Assert.AreEqual(0, gateway.SearchCalls);
    }

    private static CatalogContext Context() => new(new DbContextOptionsBuilder<CatalogContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CommerceService Service(CatalogContext context, IPayPalGateway gateway) => new(
        context, gateway, Options.Create(new PayPalOptions
        {
            ClientId = "test", ClientSecret = "test", Environment = "sandbox", Currency = "USD"
        }), new UriComposer(new CatalogSettings { CatalogBaseUrl = string.Empty }), new OrderOperationLock());

    private static PlaceOrderRequest Request() => new(
        new[] { new OrderLineRequest(1, 1) },
        new ShippingAddressRequest("1 Main St", "Test City", "CA", "US", "90210"));

    private static CardRequestDto Card() => new("Test Shopper", "4" + new string('1', 15), "2030-12", "123",
        new BillingAddressRequest("1 Main St", null, "Test City", "CA", "90210", "US"));

    private sealed class StubPayPalGateway : IPayPalGateway
    {
        public int AuthorizeCalls { get; private set; }
        public int SearchCalls { get; private set; }
        public string? LastExternalReference { get; private set; }
        public Task<ProviderAuthorization> AuthorizeAsync(string externalReference, int orderId,
            decimal amount, string currency,
            ProviderCard? card, string? vaultId, CancellationToken cancellationToken)
        {
            AuthorizeCalls++;
            LastExternalReference = externalReference;
            return Task.FromResult(new ProviderAuthorization("ORDER", "COMPLETED", "AUTH", "CREATED",
                amount, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(29), DateTimeOffset.UtcNow,
                "0000", "Y", "M"));
        }
        public Task<ProviderCapture> CaptureAsync(string externalReference, int orderId,
            string authorizationId, decimal amount,
            string currency, DateTimeOffset? authorizationCreatedAt, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<ProviderVoid> VoidAsync(string externalReference, string authorizationId,
            CancellationToken cancellationToken) =>
            throw new NotImplementedException();
        public Task<ProviderRefund> RefundAsync(string externalReference, int orderId,
            string captureId, decimal? amount, string currency,
            string idempotencyKey, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<ProviderPaymentMethod> SavePaymentMethodAsync(string buyerId, ProviderCard card,
            CancellationToken cancellationToken) => Task.FromResult(new ProviderPaymentMethod(
                "TOKEN-" + buyerId, "CUSTOMER", "VISA", "1111", "2030-12", "CREDIT"));
        public Task DeletePaymentMethodAsync(string vaultId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ProviderTransactionReport> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(new ProviderTransactionReport(Array.Empty<ProviderTransaction>(), null));
        }
    }
}
