using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// Transactions status result identifier. The outcome of the issuer's authentication.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<PaResStatus>))]
public sealed record PaResStatus : StringEnum<PaResStatus>
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

    public static PaResStatus FromValue(string value) => FromValueCore(value);
}
