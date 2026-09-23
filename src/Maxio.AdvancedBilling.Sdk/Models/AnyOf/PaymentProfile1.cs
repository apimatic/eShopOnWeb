using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(PaymentProfile1Converter))]
public record PaymentProfile1
{
    private readonly Optional<ApplePayPaymentProfile> _applePayPaymentProfileValue;

    private readonly Optional<BankAccountPaymentProfile> _bankAccountPaymentProfileValue;

    private readonly Optional<CreditCardPaymentProfile> _creditCardPaymentProfileValue;

    private readonly Optional<PaypalPaymentProfile> _paypalPaymentProfileValue;

    private PaymentProfile1(Optional<ApplePayPaymentProfile> applePayPaymentProfileValue,
        Optional<BankAccountPaymentProfile> bankAccountPaymentProfileValue,
        Optional<CreditCardPaymentProfile> creditCardPaymentProfileValue,
        Optional<PaypalPaymentProfile> paypalPaymentProfileValue)
    {
        _applePayPaymentProfileValue = applePayPaymentProfileValue;
        _bankAccountPaymentProfileValue = bankAccountPaymentProfileValue;
        _creditCardPaymentProfileValue = creditCardPaymentProfileValue;
        _paypalPaymentProfileValue = paypalPaymentProfileValue;
    }

    public static PaymentProfile1 ApplePayPaymentProfile(ApplePayPaymentProfile value) =>
        new(Optional<ApplePayPaymentProfile>.Some(value), default, default, default);

    public static PaymentProfile1 BankAccountPaymentProfile(BankAccountPaymentProfile value) =>
        new(default, Optional<BankAccountPaymentProfile>.Some(value), default, default);

    public static PaymentProfile1 CreditCardPaymentProfile(CreditCardPaymentProfile value) =>
        new(default, default, Optional<CreditCardPaymentProfile>.Some(value), default);

    public static PaymentProfile1 PaypalPaymentProfile(PaypalPaymentProfile value) =>
        new(default, default, default, Optional<PaypalPaymentProfile>.Some(value));

    public bool TryGetApplePayPaymentProfile(out ApplePayPaymentProfile value) =>
        _applePayPaymentProfileValue.TryGetValue(out value);

    public bool TryGetBankAccountPaymentProfile(out BankAccountPaymentProfile value) =>
        _bankAccountPaymentProfileValue.TryGetValue(out value);

    public bool TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile value) =>
        _creditCardPaymentProfileValue.TryGetValue(out value);

    public bool TryGetPaypalPaymentProfile(out PaypalPaymentProfile value) =>
        _paypalPaymentProfileValue.TryGetValue(out value);

    public static implicit operator PaymentProfile1(ApplePayPaymentProfile value) =>
        ApplePayPaymentProfile(value);

    public static implicit operator PaymentProfile1(BankAccountPaymentProfile value) =>
        BankAccountPaymentProfile(value);

    public static implicit operator PaymentProfile1(CreditCardPaymentProfile value) =>
        CreditCardPaymentProfile(value);

    public static implicit operator PaymentProfile1(PaypalPaymentProfile value) => PaypalPaymentProfile(value);
}

file sealed class PaymentProfile1Converter : JsonConverter<PaymentProfile1>
{
    public override PaymentProfile1 Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<ApplePayPaymentProfile>(root,
            options,
            out var applePayPaymentProfileValue))
        {
            return PaymentProfile1.ApplePayPaymentProfile(applePayPaymentProfileValue);
        }
        if (JsonSerializer.TryDeserialize<BankAccountPaymentProfile>(root,
            options,
            out var bankAccountPaymentProfileValue))
        {
            return PaymentProfile1.BankAccountPaymentProfile(bankAccountPaymentProfileValue);
        }
        if (JsonSerializer.TryDeserialize<CreditCardPaymentProfile>(root,
            options,
            out var creditCardPaymentProfileValue))
        {
            return PaymentProfile1.CreditCardPaymentProfile(creditCardPaymentProfileValue);
        }
        if (JsonSerializer.TryDeserialize<PaypalPaymentProfile>(root, options, out var paypalPaymentProfileValue))
        {
            return PaymentProfile1.PaypalPaymentProfile(paypalPaymentProfileValue);
        }
        throw new JsonException($"JSON does not match ApplePayPaymentProfile or BankAccountPaymentProfile or CreditCardPaymentProfile or PaypalPaymentProfile schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, PaymentProfile1 value, JsonSerializerOptions options)
    {
        if (value.TryGetApplePayPaymentProfile(out var applePayPaymentProfileValue))
        {
            JsonSerializer.Serialize(writer, applePayPaymentProfileValue, options);
        }
        else if (value.TryGetBankAccountPaymentProfile(out var bankAccountPaymentProfileValue))
        {
            JsonSerializer.Serialize(writer, bankAccountPaymentProfileValue, options);
        }
        else if (value.TryGetCreditCardPaymentProfile(out var creditCardPaymentProfileValue))
        {
            JsonSerializer.Serialize(writer, creditCardPaymentProfileValue, options);
        }
        else if (value.TryGetPaypalPaymentProfile(out var paypalPaymentProfileValue))
        {
            JsonSerializer.Serialize(writer, paypalPaymentProfileValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(PaymentProfile1)} contains no valid value to serialize.");
        }
    }
}
