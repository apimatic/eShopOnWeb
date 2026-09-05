using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using NSubstitute;
using Xunit;
using static Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests.PaymentServiceFixture;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class RefundingAnOrder
{
    private static async Task<(PaymentServiceFixture fixture, int orderId)> FulfilledOrder(decimal unitPrice = 50m)
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((unitPrice, 1));
        await fixture.Pay(order);
        await fixture.Service.FulfilAsync(order.Id);
        return (fixture, order.Id);
    }

    [Fact]
    public async Task ReturnsPartOfTheCapturedMoney()
    {
        var (fixture, orderId) = await FulfilledOrder();

        var result = await fixture.Service.RefundAsync(SHOPPER, orderId, 10m, "key-1", null);

        Assert.Equal(10m, result.Refund.Amount);
        Assert.Equal(RefundStatus.Completed, result.Refund.Status);
        Assert.Equal("REFUND-PAYPAL-1", result.Refund.PayPalRefundId);
        Assert.Equal(PaymentStatus.PartiallyRefunded, result.Payment.Status);
        Assert.Equal(40m, result.Payment.RefundableAmount);
    }

    [Fact]
    public async Task TheSameKeyNeverRefundsTwice()
    {
        var (fixture, orderId) = await FulfilledOrder();

        var first = await fixture.Service.RefundAsync(SHOPPER, orderId, 10m, "key-1", null);
        var again = await fixture.Service.RefundAsync(SHOPPER, orderId, 10m, "key-1", null);

        Assert.True(again.AlreadyRecorded);
        Assert.Equal(first.Refund.Id, again.Refund.Id);
        Assert.Equal(40m, again.Payment.RefundableAmount);
        await fixture.Gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoDifferentKeysAreTwoLegitimatePartialReturns()
    {
        var (fixture, orderId) = await FulfilledOrder();

        await fixture.Service.RefundAsync(SHOPPER, orderId, 10m, "key-1", null);
        var second = await fixture.Service.RefundAsync(SHOPPER, orderId, 5m, "key-2", null);

        Assert.False(second.AlreadyRecorded);
        Assert.Equal(35m, second.Payment.RefundableAmount);
        Assert.Equal(2, second.Payment.Refunds.Count);
        await fixture.Gateway.Received(2).RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CanNeverGiveBackMoreThanWasCaptured()
    {
        var (fixture, orderId) = await FulfilledOrder();

        await fixture.Service.RefundAsync(SHOPPER, orderId, 40m, "key-1", null);

        var failure = await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.RefundAsync(SHOPPER, orderId, 20m, "key-2", null));

        Assert.Contains("can still be refunded", failure.Message);
        Assert.Equal(PaymentStatus.PartiallyRefunded, fixture.PaymentFor(orderId).Status);
        await fixture.Gateway.Received(1).RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RefundingEverythingLeavesNothingToRefund()
    {
        var (fixture, orderId) = await FulfilledOrder();

        var rest = await fixture.Service.RefundAsync(SHOPPER, orderId, null, "all", null);

        Assert.Equal(50m, rest.Refund.Amount);
        Assert.Equal(PaymentStatus.FullyRefunded, rest.Payment.Status);
        Assert.Equal(0m, rest.Payment.RefundableAmount);

        await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.RefundAsync(SHOPPER, orderId, 1m, "more", null));
    }

    [Fact]
    public async Task AnOrderThatWasNotFulfilledHasNothingToRefund()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((50m, 1));
        await fixture.Pay(order);

        await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.RefundAsync(SHOPPER, order.Id, 5m, "key-1", null));

        await fixture.Gateway.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnlyTheOwnerCanRefundTheirOrder()
    {
        var (fixture, orderId) = await FulfilledOrder();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.Service.RefundAsync(SOMEONE_ELSE, orderId, 5m, "key-1", null));
    }
}

public class SavedCards
{
    [Fact]
    public async Task ASavedCardIsForgottenAtTheProcessorAsWellAsHere()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();

        await fixture.Service.DeleteSavedCardAsync(SHOPPER, card.Id);

        await fixture.Gateway.Received(1).DeleteSavedCardAsync(VAULT_ID, PAYPAL_CUSTOMER_ID,
            Arg.Any<CancellationToken>());
        Assert.Empty(fixture.Context.PaymentMethods.ToList());
    }

    [Fact]
    public async Task ASavedCardBelongsToTheShopperWhoSavedIt()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();

        Assert.Empty(await fixture.Service.GetSavedCardsAsync(SOMEONE_ELSE));
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.Service.DeleteSavedCardAsync(SOMEONE_ELSE, card.Id));
        Assert.NotEmpty(fixture.Context.PaymentMethods.ToList());
    }

    [Fact]
    public async Task ASavedCardIsDescribedSafelyAndNeverAsCardDetails()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();

        Assert.Equal(VAULT_ID, card.CardId);
        Assert.Equal("1111", card.Last4);
        Assert.Equal("VISA", card.Brand);
        Assert.DoesNotContain(CARD_NUMBER, card.Description);
        Assert.DoesNotContain(CARD_NUMBER, card.ToString());
    }
}

public class ReconcilingTheProcessorWithOurBooks
{
    private static ProcessorTransactionLine Line(string transactionId, string? referenceId = null,
        string? invoiceId = null, decimal amount = 30m, decimal? fee = null, string? customField = null,
        string status = "S", string code = "T0005")
        => new()
        {
            TransactionId = transactionId,
            ReferenceId = referenceId,
            EventCode = code,
            Status = status,
            Amount = amount,
            Currency = USD,
            FeeAmount = fee,
            InvoiceId = invoiceId,
            CustomField = customField,
            TransactionDate = new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero)
        };

    [Fact]
    public async Task MatchesByTheProcessorIdsWeCarryAndReportsBothDirections()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((30m, 1));
        await fixture.Pay(order);
        await fixture.Service.FulfilAsync(order.Id);

        fixture.PayPalReports(new[]
        {
            Line(AUTHORIZATION_ID, PAYPAL_ORDER_ID, code: "T1300", status: "P"),
            Line(CAPTURE_ID, AUTHORIZATION_ID, fee: -1.42m),
            Line("SOMEONE-ELSES", invoiceId: "eshop-pay-999-nosuchreference", amount: 99m, fee: -2m)
        });

        var report = await fixture.Service.ReconcileAsync(fixture.Clock.Now.AddDays(-1), fixture.Clock.Now.AddDays(1));

        Assert.Equal(3, report.Summary.PayPalTransactionCount);
        Assert.Equal(2, report.Summary.MatchedCount);
        Assert.Equal(1, report.Summary.OnlyInPayPalCount);
        Assert.Equal(1, report.Summary.EshopPaymentCount);
        Assert.Equal(0, report.Summary.OnlyInEshopCount);
        Assert.True(report.EshopPayments.Single().SeenInPayPalRecord);
        Assert.Equal(order.Id, report.PayPalTransactions.First(paid => paid.KnownToEshop).EshopOrderId);
        Assert.Equal(-3.42m, report.Summary.PayPalFeesAmount);
        Assert.Equal(159m, report.Summary.PayPalGrossAmount); // all three lines, including the one that is not ours
    }

    [Fact]
    public async Task MatchesOnTheReferenceWePutOnTheTransactionToo()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((30m, 1));
        await fixture.Pay(order);
        var payment = fixture.PaymentFor(order.Id);

        fixture.PayPalReports(new[] { Line("TXN-UNSEEN", invoiceId: PaymentReference.InvoiceId(payment.Id, payment.Reference, 1)) });

        var report = await fixture.Service.ReconcileAsync(fixture.Clock.Now.AddDays(-1), fixture.Clock.Now.AddDays(1));

        Assert.Equal(1, report.Summary.MatchedCount);
        Assert.Equal(order.Id, report.PayPalTransactions.Single().EshopOrderId);
    }

    [Fact]
    public async Task ARefundLineCountsAsMatchingItsPayment()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((30m, 1));
        await fixture.Pay(order);
        await fixture.Service.FulfilAsync(order.Id);
        await fixture.Service.RefundAsync(SHOPPER, order.Id, 5m, "key-1", null);

        fixture.PayPalReports(new[]
        {
            Line(AUTHORIZATION_ID, PAYPAL_ORDER_ID, code: "T1300", status: "P"),
            Line(CAPTURE_ID, AUTHORIZATION_ID, fee: -1.42m),
            Line("REFUND-PAYPAL-1", CAPTURE_ID, amount: -5m, code: "T1107")
        });

        var report = await fixture.Service.ReconcileAsync(fixture.Clock.Now.AddDays(-1), fixture.Clock.Now.AddDays(1));

        var row = report.EshopPayments.Single();
        Assert.True(row.SeenInPayPalRecord);
        Assert.Equal(5m, row.RefundedAmount);
        Assert.Equal(3, report.Summary.MatchedCount);
    }

    [Fact]
    public async Task APaymentTheProcessorHasNotReportedYetIsCalledOutAsSuch()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((30m, 1));
        await fixture.Pay(order);
        fixture.PayPalReports(Array.Empty<ProcessorTransactionLine>());

        var report = await fixture.Service.ReconcileAsync(fixture.Clock.Now.AddDays(-1), fixture.Clock.Now.AddDays(1));

        var row = report.EshopPayments.Single();
        Assert.False(row.SeenInPayPalRecord);
        Assert.Equal(1, report.Summary.OnlyInEshopCount);
        Assert.Contains(row.Issues, issue => issue.Contains("lags"));
    }

    [Fact]
    public async Task AmountsThatDoNotLineUpAreFlagged()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((30m, 1));
        await fixture.Pay(order);
        await fixture.Service.FulfilAsync(order.Id);

        fixture.PayPalReports(new[] { Line(CAPTURE_ID, AUTHORIZATION_ID, amount: 25m, fee: -1m) });

        var report = await fixture.Service.ReconcileAsync(fixture.Clock.Now.AddDays(-1), fixture.Clock.Now.AddDays(1));

        Assert.Equal(30m, report.Summary.EshopCapturedAmount);
        Assert.Equal(25m, report.Summary.PayPalGrossAmount);
        Assert.Contains(report.EshopPayments.Single().Issues, issue => issue.Contains("differs"));
    }

    [Fact]
    public async Task AReversedRangeIsRefused()
    {
        var fixture = new PaymentServiceFixture();

        await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.ReconcileAsync(fixture.Clock.Now, fixture.Clock.Now.AddDays(-1)));
    }
}

public class PlacingAnOrder
{
    [Fact]
    public async Task TotalsComeFromCatalogPricesAndStartAwaitingPayment()
    {
        var fixture = new PaymentServiceFixture();

        var order = await fixture.PlaceOrder((12.99m, 3), (0.10m, 2));

        Assert.Equal(OrderStatus.AwaitingPayment, order.Status);
        Assert.Equal(39.17m, order.Total());
        Assert.Equal(2, order.OrderItems.Count);
        Assert.Equal(SHOPPER, order.BuyerId);
    }

    [Fact]
    public async Task ACatalogItemThatIsNotThereIsNotFound()
    {
        var fixture = new PaymentServiceFixture();

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.Service.PlaceOrderAsync(SHOPPER, new[] { new PlaceOrderLine(9999, 1) }, Address()));
    }

    [Fact]
    public async Task AnEmptyOrderIsRefused()
    {
        var fixture = new PaymentServiceFixture();

        await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.PlaceOrderAsync(SHOPPER, Array.Empty<PlaceOrderLine>(), Address()));
    }

    [Fact]
    public async Task AZeroQuantityIsRefused()
    {
        var fixture = new PaymentServiceFixture();

        await Assert.ThrowsAsync<ActionNotAllowedException>(() =>
            fixture.Service.PlaceOrderAsync(SHOPPER, new[] { new PlaceOrderLine(1, 0) }, Address()));
    }
}
