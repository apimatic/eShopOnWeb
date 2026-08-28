using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NSubstitute;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.PaymentServiceTests;

public class AuthorizeOrder
{
    private readonly PaymentServiceFixture _fixture = new();

    private static readonly PaymentInstrument Card = new PaymentInstrument.OneOffCard(new CardDetails
    {
        Number = "4111111111111111",
        Expiry = "2030-01",
        SecurityCode = "123"
    });

    [Fact]
    public async Task AnAlreadyAuthorizedOrderIsNotAuthorizedASecondTime()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Authorized);
        _fixture.GivenPayment(PaymentServiceFixture.AuthorizedPayment());

        var result = await _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default);

        // The double-click case: the existing hold comes back and the processor is never called.
        Assert.Equal("PP-AUTH-1", result.AuthorizationId);
        await _fixture.Gateway.DidNotReceive()
            .AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AHoldForTheWrongAmountIsReleasedAndThePaymentFails()
    {
        var order = _fixture.GivenOrder();
        _fixture.GivenPayment(null);

        // The processor reports a hold a cent short of the order total.
        _fixture.Gateway.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER", "PP-AUTH", "CREATED",
                order.Total() - 0.01m, "USD", null));

        var failure = await Assert.ThrowsAsync<PaymentGatewayException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default));

        Assert.Contains("released", failure.Message);

        // Leaving the shopper holding a mismatched authorization would be worse than releasing it.
        await _fixture.Gateway.Received(1).VoidAsync("PP-AUTH", Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(OrderLifecycleStatus.AwaitingPayment, order.Status);
    }

    [Fact]
    public async Task AnotherShoppersOrderIsSimplyNotFound()
    {
        _fixture.GivenOrder(buyerId: PaymentServiceFixture.OtherBuyerId);
        _fixture.GivenPayment(null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default));

        await _fixture.Gateway.DidNotReceive()
            .AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnotherShoppersSavedCardIsSimplyNotFound()
    {
        _fixture.GivenOrder();
        _fixture.GivenPayment(null);

        // The card exists, but not for this buyer — so the scoped lookup returns nothing.
        _fixture.SavedCardRepository
            .FirstOrDefaultAsync(Arg.Any<SavedCardByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns((SavedCard?)null);

        await Assert.ThrowsAsync<EntityNotFoundException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1,
                new PaymentInstrument.SavedCardReference(7), default));

        await _fixture.Gateway.DidNotReceive()
            .AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASavedCardIsResolvedToItsVaultTokenOnlyAfterOwnershipIsConfirmed()
    {
        var order = _fixture.GivenOrder();
        _fixture.GivenPayment(null);
        _fixture.SavedCardRepository
            .FirstOrDefaultAsync(Arg.Any<SavedCardByIdForBuyerSpecification>(), Arg.Any<CancellationToken>())
            .Returns(new SavedCard(PaymentServiceFixture.BuyerId, "VAULT-9", "CUST-1", "VISA", "1111", "2030-01", "Demo"));

        _fixture.Gateway.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns(new AuthorizationResult("PP-ORDER", "PP-AUTH", "CREATED", order.Total(), "USD", null));

        await _fixture.Build().AuthorizeAsync(PaymentServiceFixture.BuyerId, 1,
            new PaymentInstrument.SavedCardReference(7), default);

        await _fixture.Gateway.Received(1).AuthorizeAsync(
            Arg.Is<AuthorizationRequest>(r => r.Instrument is PaymentInstrument.VaultToken
                                              && ((PaymentInstrument.VaultToken)r.Instrument).VaultId == "VAULT-9"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnknownOutcomeFreezesThePaymentInsteadOfMarkingItFailed()
    {
        _fixture.GivenOrder();
        _fixture.GivenPayment(null);

        Payment? saved = null;
        await _fixture.PaymentRepository.UpdateAsync(Arg.Do<Payment>(p => saved = p), Arg.Any<CancellationToken>());

        _fixture.Gateway.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<AuthorizationResult>(_ => throw new PaymentGatewayException(
                "connection reset", PaymentGatewayFailure.OutcomeUnknown));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default));

        // "We could not tell" is not "it failed": retrying blind could hold the money twice.
        Assert.NotNull(saved);
        Assert.True(saved!.AwaitingReconciliation);
        Assert.NotEqual(PaymentStatus.Failed, saved.Status);
    }

    [Fact]
    public async Task ADeclineLeavesThePaymentRetryableRatherThanFrozen()
    {
        _fixture.GivenOrder();
        _fixture.GivenPayment(null);

        Payment? saved = null;
        await _fixture.PaymentRepository.UpdateAsync(Arg.Do<Payment>(p => saved = p), Arg.Any<CancellationToken>());

        _fixture.Gateway.AuthorizeAsync(Arg.Any<AuthorizationRequest>(), Arg.Any<CancellationToken>())
            .Returns<AuthorizationResult>(_ => throw new PaymentGatewayException(
                "card declined", PaymentGatewayFailure.Rejected));

        await Assert.ThrowsAsync<PaymentGatewayException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default));

        Assert.NotNull(saved);
        Assert.Equal(PaymentStatus.Failed, saved!.Status);
        Assert.False(saved.AwaitingReconciliation);
    }

    [Fact]
    public async Task AnOrderThatIsNotAwaitingPaymentCannotBePaidFor()
    {
        _fixture.GivenOrder(OrderLifecycleStatus.Cancelled);
        _fixture.GivenPayment(null);

        await Assert.ThrowsAsync<OrderStateException>(() => _fixture.Build()
            .AuthorizeAsync(PaymentServiceFixture.BuyerId, 1, Card, default));
    }
}
