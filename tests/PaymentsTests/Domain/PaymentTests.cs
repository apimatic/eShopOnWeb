using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PaymentsTests.Domain;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static Payment Captured(decimal amount = 50m)
    {
        var payment = new Payment(1, "buyer", "USD", amount, "inv", Now);
        payment.BeginAuthorization("req", "inv", amount, null, "1111", Now);
        payment.RecordAuthorization(new ProviderAuthorization("ORD", "AUTH", AuthorizationOutcome.Approved, "CREATED", amount, Now, Now.AddDays(29), "VISA", "1111"), Now);
        payment.BeginCapture("cap-req", Now);
        payment.RecordCapture(new ProviderCapture("CAP", CaptureOutcome.Completed, "COMPLETED", amount, 2m, amount - 2m), Now);
        return payment;
    }

    [Fact]
    public void Refunds_can_never_exceed_what_was_captured()
    {
        var payment = Captured();
        var first = payment.AddRefund("a", 30m, "r1", Now);
        payment.RecordRefund(first, new ProviderRefund("R1", RefundOutcome.Completed, "COMPLETED", 30m), Now);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal(20m, payment.RefundableAmount);
        var ex = Assert.Throws<PaymentConflictException>(() => payment.AddRefund("b", 20.01m, "r2", Now));
        Assert.Equal("REFUND_EXCEEDS_CAPTURED", ex.Code);
    }

    [Fact]
    public void A_pending_refund_reserves_its_amount_and_a_failed_one_releases_it()
    {
        var payment = Captured();
        var pending = payment.AddRefund("a", 40m, "r1", Now);
        Assert.Equal(10m, payment.RefundableAmount);
        Assert.Throws<PaymentConflictException>(() => payment.AddRefund("b", 11m, "r2", Now));

        payment.FailRefund(pending, "refused", Now);
        Assert.Equal(50m, payment.RefundableAmount);
        Assert.Equal(PaymentStatus.Captured, payment.Status);
    }

    [Fact]
    public void Refunding_everything_marks_the_payment_refunded()
    {
        var payment = Captured();
        var all = payment.AddRefund("a", 50m, "r1", Now);
        payment.RecordRefund(all, new ProviderRefund("R1", RefundOutcome.Completed, "COMPLETED", 50m), Now);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Throws<PaymentConflictException>(() => payment.AddRefund("b", 1m, "r2", Now));
    }

    [Fact]
    public void An_authorization_for_a_different_amount_is_not_accepted()
    {
        var payment = new Payment(1, "buyer", "USD", 50m, "inv", Now);
        payment.BeginAuthorization("req", "inv", 50m, null, "1111", Now);

        var held = payment.RecordAuthorization(new ProviderAuthorization("ORD", "AUTH", AuthorizationOutcome.Approved, "CREATED", 49.99m, Now, null, null, null), Now);

        Assert.False(held);
        Assert.Equal(PaymentStatus.Declined, payment.Status);
    }

    [Fact]
    public void Nothing_can_be_refunded_before_capture()
    {
        var payment = new Payment(1, "buyer", "USD", 50m, "inv", Now);
        payment.BeginAuthorization("req", "inv", 50m, null, "1111", Now);
        payment.RecordAuthorization(new ProviderAuthorization("ORD", "AUTH", AuthorizationOutcome.Approved, "CREATED", 50m, Now, null, null, null), Now);

        Assert.Throws<PaymentConflictException>(() => payment.AddRefund("a", 1m, "r", Now));
    }

    [Theory]
    [InlineData(47.5, "USD", "47.50")]
    [InlineData(0.1, "EUR", "0.10")]
    [InlineData(1200, "JPY", "1200")]
    [InlineData(1.234, "KWD", "1.234")]
    public void Amounts_are_formatted_to_the_currency_minor_units(double amount, string currency, string expected) =>
        Assert.Equal(expected, CurrencyRules.Format((decimal)amount, currency));

    [Fact]
    public void Amounts_finer_than_the_currency_allows_are_rejected()
    {
        Assert.False(CurrencyRules.IsRepresentable(10.005m, "USD"));
        Assert.Throws<PaymentValidationException>(() => CurrencyRules.EnsureRepresentable(19.5m, "JPY", "Order total"));
    }

    [Theory]
    [InlineData("4111111111111111", true)]
    [InlineData("4111111111111112", false)]
    [InlineData("41111111", false)]
    public void Card_numbers_are_checked(string number, bool valid)
    {
        var card = new CardDetails(number, "2030-12", "123", null, null);
        if (valid)
            CardValidator.Validate(card, Now);
        else
            Assert.Throws<PaymentValidationException>(() => CardValidator.Validate(card, Now));
    }

    [Fact]
    public void Card_details_are_redacted_when_printed()
    {
        var card = new CardDetails("4111111111111111", "2030-12", "123", "Name", null);
        Assert.DoesNotContain("4111111111111111", card.ToString());
        Assert.DoesNotContain("123", card.ToString().Replace("2030-12", ""));
    }
}
