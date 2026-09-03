using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SignupProformaPreviewResponse
{
    [JsonPropertyName("proforma_invoice_preview")]
    public required SignupProformaPreview ProformaInvoicePreview { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
