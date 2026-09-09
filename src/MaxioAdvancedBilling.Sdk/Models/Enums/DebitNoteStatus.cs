using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// Current status of the debit note.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DebitNoteStatus>))]
public sealed record DebitNoteStatus : StringEnum<DebitNoteStatus>
{
    private DebitNoteStatus(string value) : base(value)
    {
    }

    public static readonly DebitNoteStatus Open = new("open");

    public static readonly DebitNoteStatus Applied = new("applied");

    public static readonly DebitNoteStatus Banished = new("banished");

    public static readonly DebitNoteStatus Paid = new("paid");

    public static DebitNoteStatus FromValue(string value) => FromValueCore(value);
}
