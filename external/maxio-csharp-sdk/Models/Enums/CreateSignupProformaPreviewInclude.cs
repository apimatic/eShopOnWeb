using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

[JsonConverter(typeof(StringEnumConverter<CreateSignupProformaPreviewInclude>))]
public sealed record CreateSignupProformaPreviewInclude : StringEnum<CreateSignupProformaPreviewInclude>
{
    private CreateSignupProformaPreviewInclude(string value) : base(value)
    {
    }

    public static readonly CreateSignupProformaPreviewInclude NextProformaInvoice = new("next_proforma_invoice");

    public static CreateSignupProformaPreviewInclude FromValue(string value) => FromValueCore(value);
}
