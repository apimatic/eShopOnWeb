using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The status for the authorized payment.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<AuthorizationStatus>))]
public sealed record AuthorizationStatus : StringEnum<AuthorizationStatus>
{
    private AuthorizationStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The authorized payment is created. No captured payments have been made for this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Created = new("CREATED");

    /// <summary>
    /// The authorized payment has one or more captures against it. The sum of these captured payments is greater than the amount of the original authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Captured = new("CAPTURED");

    /// <summary>
    /// PayPal cannot authorize funds for this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Denied = new("DENIED");

    /// <summary>
    /// A captured payment was made for the authorized payment for an amount that is less than the amount of the original authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus PartiallyCaptured = new("PARTIALLY_CAPTURED");

    /// <summary>
    /// The authorized payment was voided. No more captured payments can be made against this authorized payment.
    /// </summary>
    public static readonly AuthorizationStatus Voided = new("VOIDED");

    /// <summary>
    /// The created authorization is in pending state. For more information, see status.details.
    /// </summary>
    public static readonly AuthorizationStatus Pending = new("PENDING");

    public static AuthorizationStatus FromValue(string value) => FromValueCore(value);
}
