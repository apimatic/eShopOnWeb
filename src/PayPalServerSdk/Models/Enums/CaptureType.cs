using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The type of capture.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CaptureType>))]
public sealed record CaptureType : OpenStringEnum<CaptureType>
{
    private CaptureType(string value) : base(value)
    {
    }

    /// <summary>
    /// The outstanding balance that the subscriber must clear.
    /// </summary>
    public static readonly CaptureType OutstandingBalance = new("OUTSTANDING_BALANCE");

    public TResult Match<TResult>(Func<TResult> onOutstandingBalance, Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == OutstandingBalance => onOutstandingBalance(),
            _ => otherwise(Value)
        };

    public void Match(Action onOutstandingBalance, Action<string> otherwise)
    {
        if (this == OutstandingBalance) onOutstandingBalance();
        else otherwise(Value);
    }
}
