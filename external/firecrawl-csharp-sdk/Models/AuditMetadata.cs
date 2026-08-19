using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// User attribution included with SIEM logging events when SIEM Logging is enabled for the organization.
/// </summary>
public record AuditMetadata
{
    /// <summary>
    /// The username associated with the request.
    /// </summary>
    [JsonPropertyName("username")]
    [MaxLength(1024)]
    public required string Username { get; init; }
}
