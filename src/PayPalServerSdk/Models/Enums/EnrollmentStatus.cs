using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Status of Authentication eligibility.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<EnrollmentStatus>))]
public sealed record EnrollmentStatus : OpenStringEnum<EnrollmentStatus>
{
    private EnrollmentStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Yes. The bank is participating in 3-D Secure protocol and will return the ACSUrl.
    /// </summary>
    public static readonly EnrollmentStatus Y = new("Y");

    /// <summary>
    /// No. The bank is not participating in 3-D Secure protocol.
    /// </summary>
    public static readonly EnrollmentStatus N = new("N");

    /// <summary>
    /// Unavailable. The DS or ACS is not available for authentication at the time of the request.
    /// </summary>
    public static readonly EnrollmentStatus U = new("U");

    /// <summary>
    /// Bypass. The merchant authentication rule is triggered to bypass authentication.
    /// </summary>
    public static readonly EnrollmentStatus B = new("B");

    public TResult Match<TResult>(Func<TResult> onY,
        Func<TResult> onN,
        Func<TResult> onU,
        Func<TResult> onB,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Y => onY(),
            _ when this == N => onN(),
            _ when this == U => onU(),
            _ when this == B => onB(),
            _ => otherwise(Value)
        };

    public void Match(Action onY, Action onN, Action onU, Action onB, Action<string> otherwise)
    {
        if (this == Y) onY();
        else if (this == N) onN();
        else if (this == U) onU();
        else if (this == B) onB();
        else otherwise(Value);
    }
}
