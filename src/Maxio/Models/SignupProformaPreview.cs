using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SignupProformaPreview
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("current_proforma_invoice")]
    public ProformaInvoice? CurrentProformaInvoice { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("next_proforma_invoice")]
    public ProformaInvoice? NextProformaInvoice { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
