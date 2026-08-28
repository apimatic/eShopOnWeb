using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

public class CommercePaymentServiceTests
{
    private const string Buyer = "shopper@example.com";
    private readonly IPaymentGateway _gateway = Substitute.For<IPaymentGateway>();
    private readonly CatalogContext _db;
    private readonly CommercePaymentService _service;

    public CommercePaymentServiceTests()
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CatalogContext(options);
        _db.CatalogItems.Add(new CatalogItem(1, 1, "Test item", "Test item", 19.50m, "item.png"));
        _db.SaveChanges();
        _service = new CommercePaymentService(_db, _gateway,
            Options.Create(new PayPalOptions { Currency = "USD" }));
    }

    [Fact]
    public async Task AuthorizeCaptureAndRefundAreIdempotentAndNeverOverRefund()
    {
        var now = DateTimeOffset.UtcNow;
        _gateway.CreateOrderAsync(Arg.Any<int>(), Arg.Any<string>(), 39.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("PP-ORDER-1");
        _gateway.AuthorizeOrderAsync("PP-ORDER-1", Arg.Any<PaymentSource>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PP-ORDER-1", "COMPLETED", "AUTH-1", "CREATED",
                39.00m, "USD", now, now.AddDays(29)));
        _gateway.CaptureAsync("AUTH-1", Arg.Any<int>(), Arg.Any<string>(), 39.00m, "USD", Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-1", "COMPLETED", 39.00m, "USD", 1.47m, 37.53m, now));
        _gateway.RefundAsync("CAP-1", 10.00m, "USD", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayRefund("REF-1", "COMPLETED", 10.00m, "USD", now));

        var order = await PlaceOrder(2);
        var card = TestCard();
        var authorized = await _service.PayAsync(Buyer, order.Id,
            new PaymentSelection(card, null), CancellationToken.None);
        var authorizedAgain = await _service.PayAsync(Buyer, order.Id,
            new PaymentSelection(card, null), CancellationToken.None);
        Assert.Equal("AUTH-1", authorized.AuthorizationId);
        Assert.Equal("AUTH-1", authorizedAgain.AuthorizationId);
        await _gateway.Received(1).AuthorizeOrderAsync("PP-ORDER-1", Arg.Any<PaymentSource>(),
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var fulfilled = await _service.FulfilAsync(order.Id, CancellationToken.None);
        var fulfilledAgain = await _service.FulfilAsync(order.Id, CancellationToken.None);
        Assert.Equal(1.47m, fulfilled.PayPalFee);
        Assert.Equal(37.53m, fulfilled.NetAmount);
        Assert.Equal("CAP-1", fulfilledAgain.CaptureId);
        await _gateway.Received(1).CaptureAsync("AUTH-1", order.Id, order.PaymentReference, 39.00m, "USD",
            Arg.Any<string>(), Arg.Any<CancellationToken>());

        var firstRefund = await _service.RefundAsync(Buyer, order.Id, 10.00m, "return-1",
            CancellationToken.None);
        var replay = await _service.RefundAsync(Buyer, order.Id, 10.00m, "return-1",
            CancellationToken.None);
        Assert.False(firstRefund.Replayed);
        Assert.True(replay.Replayed);
        Assert.Equal("REF-1", replay.Refund.PayPalRefundId);
        await _gateway.Received(1).RefundAsync("CAP-1", 10.00m, "USD", Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var error = await Assert.ThrowsAsync<CommerceException>(() =>
            _service.RefundAsync(Buyer, order.Id, 29.01m, "return-2", CancellationToken.None));
        Assert.Equal("INVALID_REFUND_AMOUNT", error.Code);
    }

    [Fact]
    public async Task SavedPaymentMethodIsVisibleAndRemovableOnlyByItsOwner()
    {
        _gateway.SaveCardAsync(Arg.Any<PaymentCard>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewaySavedCard("TOKEN-1", "CUSTOMER-1", "VISA", "1111", "2030-12"));

        var saved = await _service.SavePaymentMethodAsync(Buyer, TestCard(), CancellationToken.None);
        Assert.Single(await _service.GetPaymentMethodsAsync(Buyer, CancellationToken.None));
        Assert.Empty(await _service.GetPaymentMethodsAsync("another@example.com", CancellationToken.None));

        var error = await Assert.ThrowsAsync<CommerceException>(() =>
            _service.DeletePaymentMethodAsync("another@example.com", saved.Id, CancellationToken.None));
        Assert.Equal(404, error.StatusCode);
        await _gateway.DidNotReceive().DeletePaymentTokenAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        await _service.DeletePaymentMethodAsync(Buyer, saved.Id, CancellationToken.None);
        Assert.Empty(await _service.GetPaymentMethodsAsync(Buyer, CancellationToken.None));
        await _gateway.Received(1).DeletePaymentTokenAsync("TOKEN-1", Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationExhaustsPagesAndSplitsRangesLongerThanThirtyOneDays()
    {
        var handler = new ReportingHandler();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler, false));
        var client = new PayPalClient(httpClientFactory,
            Options.Create(new PayPalOptions
            {
                ClientId = "client",
                ClientSecret = "secret",
                Environment = "Sandbox",
                Currency = "USD",
                BaseUrl = "https://paypal.test"
            }), Substitute.For<ILogger<PayPalClient>>());

        var transactions = await client.SearchTransactionsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero),
            CancellationToken.None);

        Assert.Equal(new[] { "TX-1", "TX-2", "TX-3" },
            transactions.Select(transaction => transaction.TransactionId));
        Assert.Equal(3, handler.ReportingRequestCount);
    }

    [Fact]
    public async Task FulfilmentReauthorizesAStaleAuthorizationBeforeCapture()
    {
        var staleTime = DateTimeOffset.UtcNow.AddDays(-4);
        _gateway.CreateOrderAsync(Arg.Any<int>(), Arg.Any<string>(), 19.50m, "USD",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("PP-ORDER-STALE");
        _gateway.AuthorizeOrderAsync("PP-ORDER-STALE", Arg.Any<PaymentSource>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PP-ORDER-STALE", "COMPLETED", "AUTH-STALE",
                "CREATED", 19.50m, "USD", staleTime, staleTime.AddDays(29)));
        _gateway.ReauthorizeAsync("AUTH-STALE", 19.50m, "USD", Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization(string.Empty, "COMPLETED", "AUTH-RENEWED",
                "CREATED", 19.50m, "USD", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(3)));
        _gateway.CaptureAsync("AUTH-RENEWED", Arg.Any<int>(), Arg.Any<string>(), 19.50m, "USD",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new GatewayCapture("CAP-RENEWED", "COMPLETED", 19.50m, "USD",
                1.00m, 18.50m, DateTimeOffset.UtcNow));

        var order = await PlaceOrder(1);
        await _service.PayAsync(Buyer, order.Id, new PaymentSelection(TestCard(), null),
            CancellationToken.None);
        var fulfilled = await _service.FulfilAsync(order.Id, CancellationToken.None);

        Assert.Equal("AUTH-RENEWED", fulfilled.AuthorizationId);
        Assert.Equal("CAP-RENEWED", fulfilled.CaptureId);
        await _gateway.Received(1).ReauthorizeAsync("AUTH-STALE", 19.50m, "USD",
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfilmentExplainsWhenAuthorizationIsOutsidePayPalsValidityPeriod()
    {
        var expiredTime = DateTimeOffset.UtcNow.AddDays(-30);
        _gateway.CreateOrderAsync(Arg.Any<int>(), Arg.Any<string>(), 19.50m, "USD",
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("PP-ORDER-EXPIRED");
        _gateway.AuthorizeOrderAsync("PP-ORDER-EXPIRED", Arg.Any<PaymentSource>(), Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new GatewayAuthorization("PP-ORDER-EXPIRED", "COMPLETED", "AUTH-EXPIRED",
                "CREATED", 19.50m, "USD", expiredTime, expiredTime.AddDays(29)));

        var order = await PlaceOrder(1);
        await _service.PayAsync(Buyer, order.Id, new PaymentSelection(TestCard(), null),
            CancellationToken.None);
        var error = await Assert.ThrowsAsync<CommerceException>(() =>
            _service.FulfilAsync(order.Id, CancellationToken.None));

        Assert.Equal("AUTHORIZATION_CANNOT_BE_RENEWED", error.Code);
        Assert.Contains("Ask the shopper to pay", error.Message);
        await _gateway.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    private async Task<Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order> PlaceOrder(int quantity)
    {
        var catalogItemId = await _db.CatalogItems.Select(item => item.Id).SingleAsync();
        return await _service.PlaceOrderAsync(Buyer,
            new PlaceOrderCommand(new[] { new OrderLineCommand(catalogItemId, quantity) },
                new ShippingAddressCommand("1 Main St", "San Jose", "CA", "US", "95131")),
            CancellationToken.None);
    }

    private static PaymentCard TestCard() => new("4111111111111111", "2030-12", "123",
        "Test Shopper", new PaymentBillingAddress("1 Main St", null, "San Jose", "CA", "95131", "US"));

    private sealed class ReportingHandler : HttpMessageHandler
    {
        public int ReportingRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/v1/oauth2/token")
            {
                return Json("{\"access_token\":\"token\",\"expires_in\":3600}");
            }

            ReportingRequestCount++;
            var totalPages = ReportingRequestCount <= 2 ? 2 : 1;
            var id = $"TX-{ReportingRequestCount}";
            return Json($$"""
                {
                  "transaction_details": [
                    {
                      "transaction_info": {
                        "transaction_id": "{{id}}",
                        "transaction_event_code": "T0006",
                        "transaction_status": "S"
                      }
                    }
                  ],
                  "total_pages": {{totalPages}}
                }
                """);
        }

        private static Task<HttpResponseMessage> Json(string json) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }
}
