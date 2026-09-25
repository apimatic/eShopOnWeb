using System;
using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Transactions status result identifier. The outcome of the issuer's authentication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaResStatus>))]
public sealed record PaResStatus : OpenStringEnum<PaResStatus>
{
    private PaResStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Successful authentication.
    /// </summary>
    public static readonly PaResStatus Y = new("Y");

    /// <summary>
    /// Failed authentication / account not verified / transaction denied.
    /// </summary>
    public static readonly PaResStatus N = new("N");

    /// <summary>
    /// Unable to complete authentication.
    /// </summary>
    public static readonly PaResStatus U = new("U");

    /// <summary>
    /// Successful attempts transaction.
    /// </summary>
    public static readonly PaResStatus A = new("A");

    /// <summary>
    /// Challenge required for authentication.
    /// </summary>
    public static readonly PaResStatus C = new("C");

    /// <summary>
    /// Authentication rejected (merchant must not submit for authorization).
    /// </summary>
    public static readonly PaResStatus R = new("R");

    /// <summary>
    /// Challenge required; decoupled authentication confirmed.
    /// </summary>
    public static readonly PaResStatus D = new("D");

    /// <summary>
    /// Informational only; 3DS requestor challenge preference acknowledged.
    /// </summary>
    public static readonly PaResStatus I = new("I");

    public TResult Match<TResult>(Func<TResult> onY,
        Func<TResult> onN,
        Func<TResult> onU,
        Func<TResult> onA,
        Func<TResult> onC,
        Func<TResult> onR,
        Func<TResult> onD,
        Func<TResult> onI,
        Func<string, TResult> otherwise) =>
        this switch
        {
            _ when this == Y => onY(),
            _ when this == N => onN(),
            _ when this == U => onU(),
            _ when this == A => onA(),
            _ when this == C => onC(),
            _ when this == R => onR(),
            _ when this == D => onD(),
            _ when this == I => onI(),
            _ => otherwise(Value)
        };

    public void Match(Action onY,
        Action onN,
        Action onU,
        Action onA,
        Action onC,
        Action onR,
        Action onD,
        Action onI,
        Action<string> otherwise)
    {
        if (this == Y) onY();
        else if (this == N) onN();
        else if (this == U) onU();
        else if (this == A) onA();
        else if (this == C) onC();
        else if (this == R) onR();
        else if (this == D) onD();
        else if (this == I) onI();
        else otherwise(Value);
    }
}
