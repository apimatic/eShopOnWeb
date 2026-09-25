using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Defines how and when the payment source gets vaulted.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<StoreInVaultInstruction>))]
public sealed record StoreInVaultInstruction : OpenStringEnum<StoreInVaultInstruction>
{
    private StoreInVaultInstruction(string value) : base(value)
    {
    }

    /// <summary>
    /// Defines that the payment_source will be vaulted only when at least one authorization or capture using that payment_source is successful.
    /// </summary>
    public static readonly StoreInVaultInstruction OnSuccess = new("ON_SUCCESS");

    public TResult Match<TResult>(Func<TResult> onOnSuccess, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == OnSuccess => onOnSuccess(),
            _ => otherwise(Value)
        };

    public void Match(Action onOnSuccess, Action<string> otherwise)
    {
        if (this == OnSuccess) onOnSuccess();
        else otherwise(Value);
    }
}
