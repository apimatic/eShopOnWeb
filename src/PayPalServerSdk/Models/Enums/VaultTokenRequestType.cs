using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The tokenization method that generated the ID.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<VaultTokenRequestType>))]
public sealed record VaultTokenRequestType : OpenStringEnum<VaultTokenRequestType>
{
    private VaultTokenRequestType(string value) : base(value)
    {
    }

    /// <summary>
    /// The setup token, which is a temporary reference to payment source.
    /// </summary>
    public static readonly VaultTokenRequestType SetupToken = new("SETUP_TOKEN");

    public TResult Match<TResult>(Func<TResult> onSetupToken, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == SetupToken => onSetupToken(),
            _ => otherwise(Value)
        };

    public void Match(Action onSetupToken, Action<string> otherwise)
    {
        if (this == SetupToken) onSetupToken();
        else otherwise(Value);
    }
}
