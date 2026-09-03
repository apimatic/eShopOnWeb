using System.Text.Json.Serialization;
using Maxio.Core.Enum;

namespace Maxio.Models.Enums;

/// <summary>
/// 'proforma' value is deprecated in favor of proforma_adhoc and proforma_automatic.
/// </summary>
[JsonConverter(typeof(StringEnumConverter<ProformaInvoiceRole>))]
public sealed record ProformaInvoiceRole : StringEnum<ProformaInvoiceRole>
{
    private ProformaInvoiceRole(string value) : base(value)
    {
    }

    public static readonly ProformaInvoiceRole Unset = new("unset");

    public static readonly ProformaInvoiceRole Proforma = new("proforma");

    public static readonly ProformaInvoiceRole ProformaAdhoc = new("proforma_adhoc");

    public static readonly ProformaInvoiceRole ProformaAutomatic = new("proforma_automatic");

    public static ProformaInvoiceRole FromValue(string value) => FromValueCore(value);
}
