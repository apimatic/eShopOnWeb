using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Enum;

namespace PayPalServerSdk.Models.Enums;

/// <summary>
/// The order status.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<OrderStatus>))]
public sealed record OrderStatus : StringEnum<OrderStatus>
{
    private OrderStatus(string value) : base(value)
    {
    }

    /// <summary>
    /// The order was created with the specified context.
    /// </summary>
    public static readonly OrderStatus Created = new("CREATED");

    /// <summary>
    /// The order was saved and persisted. The order status continues to be in progress until a capture is made with final_capture = true for all purchase units within the order.
    /// </summary>
    public static readonly OrderStatus Saved = new("SAVED");

    /// <summary>
    /// The customer approved the payment through the PayPal wallet or another form of guest or unbranded payment. For example, a card, bank account, or so on.
    /// </summary>
    public static readonly OrderStatus Approved = new("APPROVED");

    /// <summary>
    /// All purchase units in the order are voided.
    /// </summary>
    public static readonly OrderStatus Voided = new("VOIDED");

    /// <summary>
    /// The intent of the order was completed and a <c>payments</c> resource was created. Important: Check the payment status in <c>purchase_units[].payments.captures[].status</c> before fulfilling the order. A completed order can indicate a payment was authorized, an authorized payment was captured, or a payment was declined.
    /// </summary>
    public static readonly OrderStatus Completed = new("COMPLETED");

    /// <summary>
    /// The order requires an action from the payer (e.g. 3DS authentication). Redirect the payer to the "rel":"payer-action" HATEOAS link returned as part of the response prior to authorizing or capturing the order. Some payment sources may not return a payer-action HATEOAS link (eg. MB WAY). For these payment sources the payer-action is managed by the scheme itself (eg. through SMS, email, in-app notification, etc).
    /// </summary>
    public static readonly OrderStatus PayerActionRequired = new("PAYER_ACTION_REQUIRED");

    public static OrderStatus FromValue(string value) => FromValueCore(value);
}
