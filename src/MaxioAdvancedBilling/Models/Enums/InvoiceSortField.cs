using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<InvoiceSortField>))]
public sealed record InvoiceSortField : StringEnum<InvoiceSortField>
{
    private InvoiceSortField(string value) : base(value)
    {
    }

    public static readonly InvoiceSortField Status = new("status");

    public static readonly InvoiceSortField TotalAmount = new("total_amount");

    public static readonly InvoiceSortField DueAmount = new("due_amount");

    public static readonly InvoiceSortField CreatedAt = new("created_at");

    public static readonly InvoiceSortField UpdatedAt = new("updated_at");

    public static readonly InvoiceSortField IssueDate = new("issue_date");

    public static readonly InvoiceSortField DueDate = new("due_date");

    public static readonly InvoiceSortField Number = new("number");

    public static InvoiceSortField FromValue(string value) => FromValueCore(value);
}
