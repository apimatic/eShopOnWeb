using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Transactions status result identifier. The outcome of the issuer's authentication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ParesStatus>))]
public sealed record ParesStatus : StringEnum<ParesStatus>
{
    private ParesStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// Successful authentication.
    /// </summary>
    public static readonly ParesStatus Y = new("Y");

    /// <summary>
    /// Failed authentication / account not verified / transaction denied.
    /// </summary>
    public static readonly ParesStatus N = new("N");

    /// <summary>
    /// Unable to complete authentication.
    /// </summary>
    public static readonly ParesStatus U = new("U");

    /// <summary>
    /// Successful attempts transaction.
    /// </summary>
    public static readonly ParesStatus A = new("A");

    /// <summary>
    /// Challenge required for authentication.
    /// </summary>
    public static readonly ParesStatus C = new("C");

    /// <summary>
    /// Authentication rejected (merchant must not submit for authorization).
    /// </summary>
    public static readonly ParesStatus R = new("R");

    /// <summary>
    /// Challenge required; decoupled authentication confirmed.
    /// </summary>
    public static readonly ParesStatus D = new("D");

    /// <summary>
    /// Informational only; 3DS requestor challenge preference acknowledged.
    /// </summary>
    public static readonly ParesStatus I = new("I");

    public static ParesStatus FromValue(string value) => FromValueCore(value);
}
