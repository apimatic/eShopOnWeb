using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Current status of the credit note.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<CreditNoteStatus>))]
public sealed record CreditNoteStatus : StringEnum<CreditNoteStatus>
{
    private CreditNoteStatus(string value) : base(value)
    {
    }

    public static readonly CreditNoteStatus Open = new("open");

    public static readonly CreditNoteStatus Applied = new("applied");

    public static CreditNoteStatus FromValue(string value) => FromValueCore(value);
}
