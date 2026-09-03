using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.OneOf;

/// <summary>
/// A nested data structure detailing the method of payment
/// </summary>
[JsonConverter(typeof(InvoiceEventPayment1Converter))]
public record InvoiceEventPayment1
{
    private readonly Optional<PaymentMethodApplePay> _paymentMethodApplePayValue;

    private readonly Optional<PaymentMethodBankAccount> _paymentMethodBankAccountValue;

    private readonly Optional<PaymentMethodCreditCard> _paymentMethodCreditCardValue;

    private readonly Optional<PaymentMethodExternal> _paymentMethodExternalValue;

    private readonly Optional<PaymentMethodPaypal> _paymentMethodPaypalValue;

    private InvoiceEventPayment1(Optional<PaymentMethodApplePay> paymentMethodApplePayValue,
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

    public static InvoiceEventPayment1 PaymentMethodApplePay(PaymentMethodApplePay value) =>
        new(Optional<PaymentMethodApplePay>.Some(value), default, default, default, default);

    public static InvoiceEventPayment1 PaymentMethodBankAccount(PaymentMethodBankAccount value) =>
        new(default, Optional<PaymentMethodBankAccount>.Some(value), default, default, default);

    public static InvoiceEventPayment1 PaymentMethodCreditCard(PaymentMethodCreditCard value) =>
        new(default, default, Optional<PaymentMethodCreditCard>.Some(value), default, default);

    public static InvoiceEventPayment1 PaymentMethodExternal(PaymentMethodExternal value) =>
        new(default, default, default, Optional<PaymentMethodExternal>.Some(value), default);

    public static InvoiceEventPayment1 PaymentMethodPaypal(PaymentMethodPaypal value) =>
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

    public static implicit operator InvoiceEventPayment1(PaymentMethodApplePay value) =>
        PaymentMethodApplePay(value);

    public static implicit operator InvoiceEventPayment1(PaymentMethodBankAccount value) =>
        PaymentMethodBankAccount(value);

    public static implicit operator InvoiceEventPayment1(PaymentMethodCreditCard value) =>
        PaymentMethodCreditCard(value);

    public static implicit operator InvoiceEventPayment1(PaymentMethodExternal value) =>
        PaymentMethodExternal(value);

    public static implicit operator InvoiceEventPayment1(PaymentMethodPaypal value) =>
        PaymentMethodPaypal(value);
}

file sealed class InvoiceEventPayment1Converter : JsonConverter<InvoiceEventPayment1>
{
    public override InvoiceEventPayment1 Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProperty))
        {
            throw new JsonException("Missing required 'type' discriminator field");
        }
        var discriminator = typeProperty.GetString();
        return discriminator switch
        {
            "apple_pay" => InvoiceEventPayment1.PaymentMethodApplePay(root.Deserialize<PaymentMethodApplePay>(options)!),
            "bank_account" => InvoiceEventPayment1.PaymentMethodBankAccount(root.Deserialize<PaymentMethodBankAccount>(options)!),
            "credit_card" => InvoiceEventPayment1.PaymentMethodCreditCard(root.Deserialize<PaymentMethodCreditCard>(options)!),
            "external" => InvoiceEventPayment1.PaymentMethodExternal(root.Deserialize<PaymentMethodExternal>(options)!),
            "paypal_account" => InvoiceEventPayment1.PaymentMethodPaypal(root.Deserialize<PaymentMethodPaypal>(options)!),
            _ => throw new JsonException($"JSON does not match PaymentMethodApplePay or PaymentMethodBankAccount or PaymentMethodCreditCard or PaymentMethodExternal or PaymentMethodPaypal schemas: {root.ToString()}")
        };
    }

    public override void Write(Utf8JsonWriter writer, InvoiceEventPayment1 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(InvoiceEventPayment1)} contains no valid value to serialize.");
        }
    }
}
