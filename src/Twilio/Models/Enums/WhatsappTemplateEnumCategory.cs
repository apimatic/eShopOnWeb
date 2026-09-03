using System.Text.Json.Serialization;
using Twilio.Core.Enum;

namespace Twilio.Models.Enums;

/// <summary>
/// The Category of this WhatsApp Template. One of <c>ACCOUNT_UPDATE</c>, <c>ALERT_UPDATE</c>, <c>APPOINTMENT_UPDATE</c>, <c>AUTO_REPLY</c>, <c>ISSUE_RESOLUTION</c>, <c>PAYMENT_UPDATE</c>, <c>PERSONAL_FINANCE_UPDATE</c>, <c>RESERVATION_UPDATE</c>, <c>SHIPPING_UPDATE</c>, <c>TICKET_UPDATE</c>, <c>TRANSPORTATION_UPDATE</c>, <c>MARKETING</c>, <c>AUTHENTICATION</c>, <c>UTILITY</c>, <c>OTP</c> or <c>TRANSACTIONAL</c>.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<WhatsappTemplateEnumCategory>))]
public sealed record WhatsappTemplateEnumCategory : StringEnum<WhatsappTemplateEnumCategory>
{
    private WhatsappTemplateEnumCategory(string value) : base(value)
    {
    }

    public static readonly WhatsappTemplateEnumCategory AccountUpdate = new("ACCOUNT_UPDATE");

    public static readonly WhatsappTemplateEnumCategory AlertUpdate = new("ALERT_UPDATE");

    public static readonly WhatsappTemplateEnumCategory AutoReply = new("AUTO_REPLY");

    public static readonly WhatsappTemplateEnumCategory AppointmentUpdate = new("APPOINTMENT_UPDATE");

    public static readonly WhatsappTemplateEnumCategory IssueResolution = new("ISSUE_RESOLUTION");

    public static readonly WhatsappTemplateEnumCategory PaymentUpdate = new("PAYMENT_UPDATE");

    public static readonly WhatsappTemplateEnumCategory PersonalFinanceUpdate = new("PERSONAL_FINANCE_UPDATE");

    public static readonly WhatsappTemplateEnumCategory ReservationUpdate = new("RESERVATION_UPDATE");

    public static readonly WhatsappTemplateEnumCategory ShippingUpdate = new("SHIPPING_UPDATE");

    public static readonly WhatsappTemplateEnumCategory TicketUpdate = new("TICKET_UPDATE");

    public static readonly WhatsappTemplateEnumCategory TransportationUpdate = new("TRANSPORTATION_UPDATE");

    public static readonly WhatsappTemplateEnumCategory Marketing = new("MARKETING");

    public static readonly WhatsappTemplateEnumCategory Otp = new("OTP");

    public static readonly WhatsappTemplateEnumCategory Transactional = new("TRANSACTIONAL");

    public static readonly WhatsappTemplateEnumCategory Authentication = new("AUTHENTICATION");

    public static readonly WhatsappTemplateEnumCategory Utility = new("UTILITY");

    public static WhatsappTemplateEnumCategory FromValue(string value) => FromValueCore(value);
}
