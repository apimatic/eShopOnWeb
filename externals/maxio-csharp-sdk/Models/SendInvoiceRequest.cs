using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record SendInvoiceRequest
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("recipient_emails")]
    [MaxLength(5)]
    public IReadOnlyList<string>? RecipientEmails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cc_recipient_emails")]
    [MaxLength(5)]
    public IReadOnlyList<string>? CcRecipientEmails { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("bcc_recipient_emails")]
    [MaxLength(5)]
    public IReadOnlyList<string>? BccRecipientEmails { get; init; }

    /// <summary>
    /// Array of URLs to files to attach to the invoice email. Max 10 files, 10MB each.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("attachment_urls")]
    [MaxLength(10)]
    public IReadOnlyList<string>? AttachmentUrls { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
