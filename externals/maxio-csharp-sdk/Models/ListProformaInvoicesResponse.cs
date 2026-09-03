using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListProformaInvoicesResponse
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("proforma_invoices")]
    public IReadOnlyList<ProformaInvoice>? ProformaInvoices { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("meta")]
    public ListProformaInvoicesMeta? Meta { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
