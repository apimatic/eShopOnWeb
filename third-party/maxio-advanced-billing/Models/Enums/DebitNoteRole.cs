using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The role of the debit note.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<DebitNoteRole>))]
public sealed record DebitNoteRole : StringEnum<DebitNoteRole>
{
    private DebitNoteRole(string value) : base(value)
    {
    }

    public static readonly DebitNoteRole Chargeback = new("chargeback");

    public static readonly DebitNoteRole Refund = new("refund");

    public static DebitNoteRole FromValue(string value) => FromValueCore(value);
}
