using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.IntegrationTests.Payments;

/// <summary>
/// Unit tests for <see cref="PaymentService"/> orchestration: idempotency of pay/capture/refund,
/// refund bounds, authorization renewal at fulfilment, and reconciliation matching - all against
/// a stubbed gateway, with no PayPal traffic.
/// </summary>
public class PaymentServiceTests
{
    private static (PaymentService Service, IPaymentGateway Gateway) BuildService(decimal itemPrice)
    {
        var options = new DbContextOptionsBuilder<CatalogContext>()
            .UseInMemoryDatabase($"payments-{Guid.NewGuid()}")
            .Options;
        var context = new CatalogContext(options);
        context.CatalogItems.Add(new CatalogItem(1, 1, "Test item", "Test item", itemPrice, "pic.png"));
        context.SaveChanges();

        var orderRepository = new EfRepository<Order>(context);
        var paymentRepository = new EfRepository<Payment>(context);
        var savedCardRepository = new EfRepository<SavedCard>(context);
        var catalogRepository = new EfRepository<CatalogItem>(context);
        var uriComposer = Substitute.For<IUriComposer>();
        uriComposer.ComposePicUri(Arg.Any<string>()).Returns(x => (string)x[0]);
        var gateway = Substitute.For<IPaymentGateway>();
        gateway.Currency.Returns("USD");
        var service = new PaymentService(gateway, orderRepository, paymentRepository,
            savedCardRepository, catalogRepository, uriComposer, new LoggerAdapter<PaymentService>(NullLoggerFactory.Instance));
        return (service, gateway);
    }

    private static async Task<int> PlaceOrderAsync(PaymentService service, string buyerId)
    {
        var place = await service.PlaceOrderAsync(buyerId, new PlaceOrderInput
        {
            Items = new List<OrderItemInput> { new OrderItemInput { CatalogItemId = 1, Quantity = 1 } },
            ShipToAddress = new AddressInput { Street = "Street", City = "City", State = "ST", Country = "USA", ZipCode = "Zip" }
        });
        Assert.True(place.Succeeded, place.Error?.Message ?? "placing the order failed");
        return place.OrderId;
    }

    private static CardInput Card() => new CardInput
    {
        Name = "Test Shopper",
        Number = "4111111111111111",
        ExpiryMonth = 12,
        ExpiryYear = 2030,
        SecurityCode = "123",
        BillingAddress = new BillingAddressInput { CountryCode = "US", AddressLine1 = "1 Main St", PostalCode = "98101" }
    };

    private static GatewayResult<AuthorizeOutcome> Authorized(string authId = "AUTH-1") =>
        GatewayResult<AuthorizeOutcome>.Success(new AuthorizeOutcome
        {
            PayPalOrderId = "ORDER-1",
            AuthorizationId = authId,
            AuthorizationStatus = "CREATED",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(2)
        });

    [Fact]
    public async Task PayIsIdempotentSecondCallReturnsSameAuthorizationWithoutGatewayCall()
    {
        var (service, gateway) = BuildService(19.50m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        var orderId = await PlaceOrderAsync(service, "buyer");

        var first = await service.PayOrderAsync("buyer", orderId, null, Card());
        var second = await service.PayOrderAsync("buyer", orderId, null, Card());

        Assert.True(first.Succeeded);
        Assert.Equal(PaymentStatus.Authorized, first.Payment!.Status);
        Assert.True(second.Succeeded);
        Assert.Equal(first.Payment.PayPalAuthorizationId, second.Payment!.PayPalAuthorizationId);
        await gateway.Received(1).AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<CardInput>(), Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task PayWithAnotherShoppersPaymentMethodIsRefused()
    {
        var (service, gateway) = BuildService(10m);
        gateway.VaultCardAsync(Arg.Any<string>(), Arg.Any<CardInput>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<VaultOutcome>.Success(new VaultOutcome
            {
                TokenId = "TOKEN-1", CustomerId = "CUST-1", Brand = "VISA", LastDigits = "1111"
            }));
        var orderId = await PlaceOrderAsync(service, "buyer");
        var saved = await service.SaveCardAsync("buyer", Card());
        Assert.True(saved.Succeeded);

        // A different shopper must not be able to pay with it.
        var result = await service.PayOrderAsync("intruder", orderId, saved.Card!.Id, null);
        Assert.False(result.Succeeded);
        Assert.Equal(PaymentErrorType.NotFound, result.Error!.Type);
    }

    [Fact]
    public async Task RefundWithSameIdempotencyKeyNeverRefundsTwice()
    {
        var (service, gateway) = BuildService(19.50m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Success(new CaptureOutcome
            {
                CaptureId = "CAP-1", CaptureStatus = "COMPLETED", CapturedAmount = 19.50m,
                Currency = "USD", PayPalFee = 1.26m, NetAmount = 18.24m
            }));
        var orderId = await PlaceOrderAsync(service, "buyer");
        var pay = await service.PayOrderAsync("buyer", orderId, null, Card());
        var fulfil = await service.FulfilOrderAsync(orderId);
        Assert.True(fulfil.Succeeded);

        gateway.RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<RefundOutcome>.Success(new RefundOutcome
            {
                RefundId = "REF-1", Status = "COMPLETED", Amount = 5m, Currency = "USD", TotalRefundedAmount = 5m
            }));

        var first = await service.RefundOrderAsync(orderId, 5m, "key-1");
        var second = await service.RefundOrderAsync(orderId, 5m, "key-1");

        Assert.True(first.Succeeded);
        Assert.Equal(first.Refund!.Id, second.Refund!.Id);
        await gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal?>(),
            Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>());
    }

    [Fact]
    public async Task RefundBeyondCapturedAmountIsRefused()
    {
        var (service, gateway) = BuildService(10m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Success(new CaptureOutcome
            {
                CaptureId = "CAP-1", CaptureStatus = "COMPLETED", CapturedAmount = 10m, Currency = "USD"
            }));
        var orderId = await PlaceOrderAsync(service, "buyer");
        await service.PayOrderAsync("buyer", orderId, null, Card());
        Assert.True((await service.FulfilOrderAsync(orderId)).Succeeded);

        var result = await service.RefundOrderAsync(orderId, 99m, "key-too-much");

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task FulfilRenewsStaleAuthorizationAndCaptures()
    {
        var (service, gateway) = BuildService(12m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        var orderId = await PlaceOrderAsync(service, "buyer");
        var pay = await service.PayOrderAsync("buyer", orderId, null, Card());
        Assert.True(pay.Succeeded);

        // First capture attempt reports the authorization is stale; the renewal succeeds
        // and the retry captures the renewed authorization.
        gateway.CaptureAsync(Arg.Any<string>(), "AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Failure(
                new PaymentError(PaymentErrorType.StaleAuthorization, "AUTHORIZATION_EXPIRED")));
        gateway.ReauthorizeAsync(Arg.Any<string>(), "AUTH-1", Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<ReauthorizeOutcome>.Success(new ReauthorizeOutcome
            {
                AuthorizationId = "AUTH-2", AuthorizationStatus = "CREATED",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(3)
            }));
        gateway.CaptureAsync(Arg.Any<string>(), "AUTH-2", Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Success(new CaptureOutcome
            {
                CaptureId = "CAP-1", CaptureStatus = "COMPLETED", CapturedAmount = 12m,
                Currency = "USD", PayPalFee = 0.78m, NetAmount = 11.22m
            }));

        var result = await service.FulfilOrderAsync(orderId);

        Assert.True(result.Succeeded);
        Assert.Equal(PaymentStatus.Captured, result.Payment!.Status);
        Assert.Equal("AUTH-2", result.Payment.PayPalAuthorizationId);
        Assert.Equal("CAP-1", result.Payment.PayPalCaptureId);
        Assert.Equal(0.78m, result.Payment.PayPalFee);
        Assert.Equal(11.22m, result.Payment.NetAmount);
    }

    [Fact]
    public async Task FulfilWhenRenewalFailsReportsActionableStaleAuthorization()
    {
        var (service, gateway) = BuildService(12m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        var orderId = await PlaceOrderAsync(service, "buyer");
        Assert.True((await service.PayOrderAsync("buyer", orderId, null, Card())).Succeeded);

        gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Failure(
                new PaymentError(PaymentErrorType.StaleAuthorization, "AUTHORIZATION_EXPIRED")));
        gateway.ReauthorizeAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<ReauthorizeOutcome>.Failure(
                new PaymentError(PaymentErrorType.Declined, "cannot reauthorize")));

        var result = await service.FulfilOrderAsync(orderId);

        Assert.False(result.Succeeded);
        Assert.Equal(PaymentErrorType.StaleAuthorization, result.Error!.Type);
        Assert.Contains("could not be renewed", result.Error!.Message);
    }

    [Fact]
    public async Task ReconciliationMatchesByInvoiceKeyAndPayPalId()
    {
        var (service, gateway) = BuildService(20m);
        gateway.AuthorizeAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CardInput>(),
                Arg.Any<string>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(Authorized());
        gateway.CaptureAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<CaptureOutcome>.Success(new CaptureOutcome
            {
                CaptureId = "CAP-1", CaptureStatus = "COMPLETED", CapturedAmount = 20m, Currency = "USD"
            }));
        var orderId = await PlaceOrderAsync(service, "buyer");
        var pay = await service.PayOrderAsync("buyer", orderId, null, Card());
        var fulfil = await service.FulfilOrderAsync(orderId);
        Assert.True(fulfil.Succeeded);
        var paymentKey = pay.Payment!.PaymentKey;

        gateway.SearchTransactionsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(),
                Arg.Any<System.Threading.CancellationToken>())
            .Returns(GatewayResult<ReconciliationResult>.Success(new ReconciliationResult
            {
                Transactions = new List<ReconciliationTransaction>
                {
                    // PayPal re-formats the invoice id (uppercased prefix, inserted unit);
                    // match on the key suffix.
                    new ReconciliationTransaction
                    {
                        TransactionId = "TXN-BY-INVOICE", EventCode = "T0005", Status = "S",
                        Amount = 20m, Currency = "USD",
                        InvoiceId = $"ESHOP-1-{paymentKey}"
                    },
                    new ReconciliationTransaction
                    {
                        TransactionId = "CAP-1", EventCode = "T0005", Status = "S",
                        Amount = 20m, Currency = "USD", InvoiceId = null
                    },
                    new ReconciliationTransaction
                    {
                        TransactionId = "TXN-UNMATCHED", EventCode = "T0005", Status = "S",
                        Amount = 1m, Currency = "USD", InvoiceId = null
                    }
                },
                LastRefreshedDatetime = "2026-09-05T12:00:00Z"
            }));

        var from = DateTimeOffset.UtcNow.AddHours(-1);
        var to = DateTimeOffset.UtcNow.AddMinutes(1);
        var report = await service.ReconcileAsync(from, to);

        Assert.True(report.Succeeded);
        Assert.Equal(3, report.Value!.PayPalTransactions.Count);
        // One local capture event, matched both by invoice key and by PayPal capture id.
        Assert.Equal(1, report.Value!.ShopPayments.Count);
        Assert.True(report.Value!.ShopPayments[0].Matched);
        Assert.Equal(1, report.Value!.MatchedCount);
        Assert.Empty(report.Value!.ShopOnly);
        Assert.Single(report.Value!.PayPalOnly);
        Assert.Equal("TXN-UNMATCHED", report.Value!.PayPalOnly[0].TransactionId);
        Assert.Equal("2026-09-05T12:00:00Z", report.Value!.LastRefreshedDatetime);
    }
}
