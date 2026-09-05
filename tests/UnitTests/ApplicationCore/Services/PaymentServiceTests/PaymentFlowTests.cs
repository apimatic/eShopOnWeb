using System;
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

public class PayingForAnOrder
{
    [Fact]
    public async Task HoldsTheOrderTotalToTheCent()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((12.50m, 2), (3.75m, 1));

        var result = await fixture.Pay(order);

        Assert.Equal(PaymentStatus.Authorized, result.Payment.Status);
        Assert.Equal(OrderStatus.Authorized, result.Order.Status);
        Assert.Equal(28.75m, result.Payment.Amount);
        Assert.Equal(USD, result.Payment.Currency);
        await fixture.Gateway.Received(1).AuthorizeAsync(
            Arg.Is<AuthorizePaymentRequest>(request => request.Amount == 28.75m && request.Currency == USD),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayingTwiceDoesNotHoldTheMoneyTwice()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));

        var first = await fixture.Pay(order);
        var second = await fixture.Pay(order);

        Assert.True(second.AlreadyRecorded);
        Assert.Equal(first.Payment.AuthorizationId, second.Payment.AuthorizationId);
        await fixture.Gateway.Received(1).AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APaymentThatWasRefusedLeavesTheOrderPayableAgain()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));
        fixture.FailNextAuthorizations.Enqueue(new CardDeclinedException("declined by the issuer"));

        await Assert.ThrowsAsync<CardDeclinedException>(() => fixture.Pay(order));

        Assert.Equal(OrderStatus.AwaitingPayment, fixture.Context.Orders.Single(o => o.Id == order.Id).Status);
        Assert.Equal(PaymentStatus.Declined, fixture.PaymentFor(order.Id).Status);

        var retried = await fixture.Pay(order);

        Assert.Equal(PaymentStatus.Authorized, retried.Payment.Status);
        Assert.Equal(2, retried.Payment.AuthorizationAttempts);
        await fixture.Gateway.Received(2).AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoPaysArrivingTogetherStillHoldOnce()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));

        var both = await Task.WhenAll(
            fixture.Service.PayAsync(SHOPPER, order.Id, Card(), null),
            fixture.Service.PayAsync(SHOPPER, order.Id, Card(), null));

        Assert.Equal(1, both.Count(result => !result.AlreadyRecorded));
        await fixture.Gateway.Received(1).AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EachHoldAttemptCarriesItsOwnReferences()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));
        fixture.FailNextAuthorizations.Enqueue(new PaymentProcessorException("processor offline"));

        await Assert.ThrowsAsync<PaymentProcessorException>(() => fixture.Pay(order));
        await fixture.Pay(order);

        var requests = fixture.Gateway.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IPaymentGateway.AuthorizeAsync))
            .Select(call => call.GetArguments()[0] as AuthorizePaymentRequest)
            .ToList();

        Assert.Equal(2, requests.Count);
        Assert.NotEqual(requests[0].InvoiceId, requests[1].InvoiceId);
        Assert.Equal(requests[0].CustomId, requests[1].CustomId);
    }

    [Fact]
    public async Task CannotPayForSomebodyElsesOrder()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.Service.PayAsync(SOMEONE_ELSE, order.Id, Card(), null));

        await fixture.Gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CannotPayWithSomebodyElsesSavedCard()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();
        var order = await fixture.PlaceOrder((10m, 1));

        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            fixture.Service.PayAsync(SOMEONE_ELSE, order.Id, null, card.Id));

        await fixture.Gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PayingWithASavedCardSendsItsTokenAndNotACardNumber()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();
        var order = await fixture.PlaceOrder((10m, 1));

        await fixture.Service.PayAsync(SHOPPER, order.Id, null, card.Id);

        await fixture.Gateway.Received(1).AuthorizeAsync(
            Arg.Is<AuthorizePaymentRequest>(request =>
                request.Card == null && request.SavedCard != null && request.SavedCard.VaultId == VAULT_ID),
            Arg.Any<CancellationToken>());

        var payment = fixture.PaymentFor(order.Id);
        Assert.Equal(card.Id, payment.PaymentMethodId);
        Assert.Equal(VAULT_ID, payment.CardVaultId);
        Assert.Equal(PAYPAL_CUSTOMER_ID, payment.PayPalCustomerId);
    }

    [Fact]
    public async Task NoCardNumberIsEverStored()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));
        await fixture.Pay(order);
        await fixture.SavedCard();

        var stored = fixture.Context.ChangeTracker.Entries()
            .SelectMany(entry => entry.Properties)
            .Select(property => property.CurrentValue)
            .OfType<object>()
            .Select(value => value.ToString())
            .ToList();

        Assert.Contains(stored, value => value != null && value.Contains("1111"));
        Assert.DoesNotContain(stored, value => value != null && value.Contains(CARD_NUMBER));
        Assert.DoesNotContain(stored, value => value != null && value.Contains("security"));
    }

    [Theory]
    [InlineData("1234", "2030-11", "123")]
    [InlineData("4111111111111111", "11-2030", "123")]
    [InlineData("4111111111111111", "2020-01", "123")]
    [InlineData("4111111111111111", "2030-13", "123")]
    [InlineData("4111111111111111", "2030-11", "12")]
    public async Task AnUnusableCardIsRejectedBeforeMoneyIsHeld(string number, string expiry, string securityCode)
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((10m, 1));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Service.PayAsync(SHOPPER, order.Id, Card(number, expiry, securityCode), null));

        await fixture.Gateway.DidNotReceive().AuthorizeAsync(Arg.Any<AuthorizePaymentRequest>(),
            Arg.Any<CancellationToken>());
    }
}

public class FulfillingAnOrder
{
    [Fact]
    public async Task TakesTheMoneyAndReportsFeeAndNet()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);

        var result = await fixture.Service.FulfilAsync(order.Id);

        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        Assert.Equal(PaymentStatus.Captured, result.Payment.Status);
        Assert.Equal(20m, result.Payment.CapturedAmount);
        Assert.Equal(fixture.Fee, result.Payment.FeeAmount);
        Assert.Equal(18.58m, result.Payment.NetAmount);
        Assert.Equal(CAPTURE_ID, result.Payment.CaptureId);
        await fixture.Gateway.Received(1).CaptureAsync(AUTHORIZATION_ID, 20m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FulfillingTwiceDoesNotTakeTheMoneyTwice()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);

        await fixture.Service.FulfilAsync(order.Id);
        var again = await fixture.Service.FulfilAsync(order.Id);

        Assert.True(again.AlreadyRecorded);
        await fixture.Gateway.Received(1).CaptureAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOrderThatWasNeverPaidCannotBeFulfilled()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));

        await Assert.ThrowsAsync<ActionNotAllowedException>(() => fixture.Service.FulfilAsync(order.Id));
    }

    [Fact]
    public async Task AHoldThatHasGoneStaleIsRenewedRatherThanFailing()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);
        fixture.HoldIsGone();

        var result = await fixture.Service.FulfilAsync(order.Id);

        Assert.True(result.RenewedHold);
        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        await fixture.Gateway.Received(1).ReauthorizeAsync(AUTHORIZATION_ID, 20m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await fixture.Gateway.Received(1).CaptureAsync(RENEWED_AUTHORIZATION_ID, 20m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        var payment = fixture.PaymentFor(order.Id);
        Assert.Equal(RENEWED_AUTHORIZATION_ID, payment.AuthorizationId);
        Assert.Equal(AUTHORIZATION_ID, payment.RenewedFromAuthorizationId);
        Assert.Equal(1, payment.RenewalCount);
        Assert.Contains("renewed", result.Note);
    }

    [Fact]
    public async Task AHoldThatTheClockSaysIsLiveButTheProcessorSaysHasExpiredIsRenewedAndTaken()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);
        fixture.CaptureIsRefusedOnce("AUTHORIZATION_EXPIRED");

        var result = await fixture.Service.FulfilAsync(order.Id);

        Assert.True(result.RenewedHold);
        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        await fixture.Gateway.Received(2).CaptureAsync(Arg.Any<string>(), 20m, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AHoldThatCannotBeRenewedSaysWhatTheOperatorMustDo()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);
        fixture.HoldCannotBeFound();
        fixture.ReauthorizeIsRefused("REAUTHORIZATION_TOO_LATE");

        var failure = await Assert.ThrowsAsync<PaymentRenewalFailedException>(() =>
            fixture.Service.FulfilAsync(order.Id));

        Assert.Contains("cannot be renewed", failure.Message);
        Assert.Contains($"/api/orders/{order.Id}/pay", failure.Message);
        Assert.DoesNotContain(CARD_NUMBER, failure.Message);
        Assert.Equal(OrderStatus.Authorized, fixture.Context.Orders.Single(o => o.Id == order.Id).Status);
        await fixture.Gateway.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default, default!);
    }

    [Fact]
    public async Task ASavedCardIsHeldAgainWhenTheOriginalHoldCannotBeRenewed()
    {
        var fixture = new PaymentServiceFixture();
        var card = await fixture.SavedCard();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Service.PayAsync(SHOPPER, order.Id, null, card.Id);
        fixture.HoldCannotBeFound();
        fixture.ReauthorizeIsRefused("REAUTHORIZATION_NOT_SUPPORTED_FOR_PAYMENT_SOURCE");

        var result = await fixture.Service.FulfilAsync(order.Id);

        Assert.True(result.RenewedHold);
        Assert.Equal(OrderStatus.Fulfilled, result.Order.Status);
        Assert.Equal(1, fixture.PaymentFor(order.Id).RenewalCount);
        await fixture.Gateway.Received(2).AuthorizeAsync(
            Arg.Is<AuthorizePaymentRequest>(request => request.SavedCard != null), Arg.Any<CancellationToken>());
    }
}

public class CancellingAnOrder
{
    [Fact]
    public async Task ReleasesTheHeldMoneySoNothingIsTaken()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);

        var result = await fixture.Service.CancelAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Order.Status);
        Assert.Equal(PaymentStatus.Voided, result.Payment.Status);
        Assert.Null(fixture.PaymentFor(order.Id).CaptureId);
        await fixture.Gateway.Received(1).VoidAsync(AUTHORIZATION_ID, Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await fixture.Gateway.DidNotReceiveWithAnyArgs().CaptureAsync(default!, default, default!);
    }

    [Fact]
    public async Task CancellingTwiceDoesNotVoidTwice()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);

        await fixture.Service.CancelAsync(order.Id);
        var again = await fixture.Service.CancelAsync(order.Id);

        Assert.True(again.AlreadyRecorded);
        await fixture.Gateway.Received(1).VoidAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnOrderAwaitingPaymentIsSimplyCalledOff()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));

        var result = await fixture.Service.CancelAsync(order.Id);

        Assert.Equal(OrderStatus.Cancelled, result.Order.Status);
        Assert.Null(result.Payment);
        await fixture.Gateway.DidNotReceive().VoidAsync(Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFulfilledOrderMustBeRefundedNotCancelled()
    {
        var fixture = new PaymentServiceFixture();
        var order = await fixture.PlaceOrder((20m, 1));
        await fixture.Pay(order);
        await fixture.Service.FulfilAsync(order.Id);

        await Assert.ThrowsAsync<ActionNotAllowedException>(() => fixture.Service.CancelAsync(order.Id));
    }
}
