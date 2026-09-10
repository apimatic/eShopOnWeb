using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

/// <summary>
/// A nested data structure detailing the method of payment
/// </summary>
[JsonConverter(typeof(InvoiceEventPaymentConverter))]
public record InvoiceEventPayment
{
    private readonly Optional<PaymentMethodApplePay> _paymentMethodApplePayValue;

    private readonly Optional<PaymentMethodBankAccount> _paymentMethodBankAccountValue;

    private readonly Optional<PaymentMethodCreditCard> _paymentMethodCreditCardValue;

    private readonly Optional<PaymentMethodExternal> _paymentMethodExternalValue;

    private readonly Optional<PaymentMethodPaypal> _paymentMethodPaypalValue;

    private InvoiceEventPayment(Optional<PaymentMethodApplePay> paymentMethodApplePayValue,
        Optional<PaymentMethodBankAccount> paymentMethodBankAccountValue,
        Optional<PaymentMethodCreditCard> paymentMethodCreditCardValue,
        Optional<PaymentMethodExternal> paymentMethodExternalValue,
        Optional<PaymentMethodPaypal> paymentMethodPaypalValue)
    {
        _paymentMethodApplePayValue = paymentMethodApplePayValue;
        _paymentMethodBankAccountValue = paymentMethodBankAccountValue;
        _paymentMethodCreditCardValue = paymentMethodCreditCardValue;
        _paymentMethodExternalValue = paymentMethodExternalValue;
        _paymentMethodPaypalValue = paymentMethodPaypalValue;
    }

    public static InvoiceEventPayment PaymentMethodApplePay(PaymentMethodApplePay value) =>
        new(Optional<PaymentMethodApplePay>.Some(value), default, default, default, default);

    public static InvoiceEventPayment PaymentMethodBankAccount(PaymentMethodBankAccount value) =>
        new(default, Optional<PaymentMethodBankAccount>.Some(value), default, default, default);

    public static InvoiceEventPayment PaymentMethodCreditCard(PaymentMethodCreditCard value) =>
        new(default, default, Optional<PaymentMethodCreditCard>.Some(value), default, default);

    public static InvoiceEventPayment PaymentMethodExternal(PaymentMethodExternal value) =>
        new(default, default, default, Optional<PaymentMethodExternal>.Some(value), default);

    public static InvoiceEventPayment PaymentMethodPaypal(PaymentMethodPaypal value) =>
        new(default, default, default, default, Optional<PaymentMethodPaypal>.Some(value));

    public bool TryGetPaymentMethodApplePay(out PaymentMethodApplePay value) =>
        _paymentMethodApplePayValue.TryGetValue(out value);

    public bool TryGetPaymentMethodBankAccount(out PaymentMethodBankAccount value) =>
        _paymentMethodBankAccountValue.TryGetValue(out value);

    public bool TryGetPaymentMethodCreditCard(out PaymentMethodCreditCard value) =>
        _paymentMethodCreditCardValue.TryGetValue(out value);

    public bool TryGetPaymentMethodExternal(out PaymentMethodExternal value) =>
        _paymentMethodExternalValue.TryGetValue(out value);

    public bool TryGetPaymentMethodPaypal(out PaymentMethodPaypal value) =>
        _paymentMethodPaypalValue.TryGetValue(out value);

    public static implicit operator InvoiceEventPayment(PaymentMethodApplePay value) =>
        PaymentMethodApplePay(value);

    public static implicit operator InvoiceEventPayment(PaymentMethodBankAccount value) =>
        PaymentMethodBankAccount(value);

    public static implicit operator InvoiceEventPayment(PaymentMethodCreditCard value) =>
        PaymentMethodCreditCard(value);

    public static implicit operator InvoiceEventPayment(PaymentMethodExternal value) =>
        PaymentMethodExternal(value);

    public static implicit operator InvoiceEventPayment(PaymentMethodPaypal value) =>
        PaymentMethodPaypal(value);
}

file sealed class InvoiceEventPaymentConverter : JsonConverter<InvoiceEventPayment>
{
    public override InvoiceEventPayment Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<PaymentMethodApplePay>(root, options, out var paymentMethodApplePayValue))
        {
            return InvoiceEventPayment.PaymentMethodApplePay(paymentMethodApplePayValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentMethodBankAccount>(root,
            options,
            out var paymentMethodBankAccountValue))
        {
            return InvoiceEventPayment.PaymentMethodBankAccount(paymentMethodBankAccountValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentMethodCreditCard>(root,
            options,
            out var paymentMethodCreditCardValue))
        {
            return InvoiceEventPayment.PaymentMethodCreditCard(paymentMethodCreditCardValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentMethodExternal>(root, options, out var paymentMethodExternalValue))
        {
            return InvoiceEventPayment.PaymentMethodExternal(paymentMethodExternalValue);
        }
        if (JsonSerializer.TryDeserialize<PaymentMethodPaypal>(root, options, out var paymentMethodPaypalValue))
        {
            return InvoiceEventPayment.PaymentMethodPaypal(paymentMethodPaypalValue);
        }
        throw new JsonException($"JSON does not match PaymentMethodApplePay or PaymentMethodBankAccount or PaymentMethodCreditCard or PaymentMethodExternal or PaymentMethodPaypal schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, InvoiceEventPayment value, JsonSerializerOptions options)
    {
        if (value.TryGetPaymentMethodApplePay(out var paymentMethodApplePayValue))
        {
            JsonSerializer.Serialize(writer, paymentMethodApplePayValue, options);
        }
        else if (value.TryGetPaymentMethodBankAccount(out var paymentMethodBankAccountValue))
        {
            JsonSerializer.Serialize(writer, paymentMethodBankAccountValue, options);
        }
        else if (value.TryGetPaymentMethodCreditCard(out var paymentMethodCreditCardValue))
        {
            JsonSerializer.Serialize(writer, paymentMethodCreditCardValue, options);
        }
        else if (value.TryGetPaymentMethodExternal(out var paymentMethodExternalValue))
        {
            JsonSerializer.Serialize(writer, paymentMethodExternalValue, options);
        }
        else if (value.TryGetPaymentMethodPaypal(out var paymentMethodPaypalValue))
        {
            JsonSerializer.Serialize(writer, paymentMethodPaypalValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(InvoiceEventPayment)} contains no valid value to serialize.");
        }
    }
}
