using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record ListCreditNotesResponse
{
    [JsonPropertyName("credit_notes")]
    public required IReadOnlyList<CreditNote> CreditNotes { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
