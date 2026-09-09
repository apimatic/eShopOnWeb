using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceDateField>))]
public sealed record InvoiceDateField : StringEnum<InvoiceDateField>
{
    private InvoiceDateField(string value) : base(value)
    {
    }

    public static readonly InvoiceDateField CreatedAt = new("created_at");

    public static readonly InvoiceDateField DueDate = new("due_date");

    public static readonly InvoiceDateField IssueDate = new("issue_date");

    public static readonly InvoiceDateField UpdatedAt = new("updated_at");

    public static readonly InvoiceDateField PaidDate = new("paid_date");

    public static InvoiceDateField FromValue(string value) => FromValueCore(value);
}
