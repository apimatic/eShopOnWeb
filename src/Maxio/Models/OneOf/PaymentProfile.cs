using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.OneOf;

[JsonConverter(typeof(PaymentProfileConverter))]
public record PaymentProfile
{
    private readonly Optional<ApplePayPaymentProfile> _applePayPaymentProfileValue;

    private readonly Optional<BankAccountPaymentProfile> _bankAccountPaymentProfileValue;

    private readonly Optional<CreditCardPaymentProfile> _creditCardPaymentProfileValue;

    private readonly Optional<PaypalPaymentProfile> _paypalPaymentProfileValue;

    private PaymentProfile(Optional<ApplePayPaymentProfile> applePayPaymentProfileValue,
        Optional<BankAccountPaymentProfile> bankAccountPaymentProfileValue,
        Optional<CreditCardPaymentProfile> creditCardPaymentProfileValue,
        Optional<PaypalPaymentProfile> paypalPaymentProfileValue)
    {
        _applePayPaymentProfileValue = applePayPaymentProfileValue;
        _bankAccountPaymentProfileValue = bankAccountPaymentProfileValue;
        _creditCardPaymentProfileValue = creditCardPaymentProfileValue;
        _paypalPaymentProfileValue = paypalPaymentProfileValue;
    }

    public static PaymentProfile ApplePayPaymentProfile(ApplePayPaymentProfile value) =>
        new(Optional<ApplePayPaymentProfile>.Some(value), default, default, default);

    public static PaymentProfile BankAccountPaymentProfile(BankAccountPaymentProfile value) =>
        new(default, Optional<BankAccountPaymentProfile>.Some(value), default, default);

    public static PaymentProfile CreditCardPaymentProfile(CreditCardPaymentProfile value) =>
        new(default, default, Optional<CreditCardPaymentProfile>.Some(value), default);

    public static PaymentProfile PaypalPaymentProfile(PaypalPaymentProfile value) =>
        new(default, default, default, Optional<PaypalPaymentProfile>.Some(value));

    public bool TryGetApplePayPaymentProfile(out ApplePayPaymentProfile value) =>
        _applePayPaymentProfileValue.TryGetValue(out value);

    public bool TryGetBankAccountPaymentProfile(out BankAccountPaymentProfile value) =>
        _bankAccountPaymentProfileValue.TryGetValue(out value);

    public bool TryGetCreditCardPaymentProfile(out CreditCardPaymentProfile value) =>
        _creditCardPaymentProfileValue.TryGetValue(out value);

    public bool TryGetPaypalPaymentProfile(out PaypalPaymentProfile value) =>
        _paypalPaymentProfileValue.TryGetValue(out value);

    public static implicit operator PaymentProfile(ApplePayPaymentProfile value) =>
        ApplePayPaymentProfile(value);

    public static implicit operator PaymentProfile(BankAccountPaymentProfile value) =>
        BankAccountPaymentProfile(value);

    public static implicit operator PaymentProfile(CreditCardPaymentProfile value) =>
        CreditCardPaymentProfile(value);

    public static implicit operator PaymentProfile(PaypalPaymentProfile value) => PaypalPaymentProfile(value);
}

file sealed class PaymentProfileConverter : JsonConverter<PaymentProfile>
{
    public override PaymentProfile Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("payment_type", out var typeProperty))
        {
            throw new JsonException("Missing required 'payment_type' discriminator field");
        }
        var discriminator = typeProperty.GetString();
        return discriminator switch
        {
            "apple_pay" => PaymentProfile.ApplePayPaymentProfile(root.Deserialize<ApplePayPaymentProfile>(options)!),
            "bank_account" => PaymentProfile.BankAccountPaymentProfile(root.Deserialize<BankAccountPaymentProfile>(options)!),
            "credit_card" => PaymentProfile.CreditCardPaymentProfile(root.Deserialize<CreditCardPaymentProfile>(options)!),
            "paypal_account" => PaymentProfile.PaypalPaymentProfile(root.Deserialize<PaypalPaymentProfile>(options)!),
            _ => throw new JsonException($"JSON does not match ApplePayPaymentProfile or BankAccountPaymentProfile or CreditCardPaymentProfile or PaypalPaymentProfile schemas: {root.ToString()}")
        };
    }

    public override void Write(Utf8JsonWriter writer, PaymentProfile value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(PaymentProfile)} contains no valid value to serialize.");
        }
    }
}
