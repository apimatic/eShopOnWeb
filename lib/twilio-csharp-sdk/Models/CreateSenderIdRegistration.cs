using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Twilio.Core.Models;
using Twilio.Models.Enums;

namespace Twilio.Models;

public record CreateSenderIdRegistration
{
    /// <summary>
    /// List of Iso countries
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("iso_countries")]
    public IReadOnlyList<string>? IsoCountries { get; init; }

    /// <summary>
    /// Whether registering on behalf of subsidiary
    /// </summary>
    [JsonPropertyName("company_subsidiary")]
    public required bool CompanySubsidiary { get; init; }

    /// <summary>
    /// Business profile Bundle sid used for the application
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("business_profile_sid")]
    [StringLength(34, MinimumLength = 34)]
    [RegularExpression("^BU[0-9a-fA-F]{32}$")]
    public string? BusinessProfileSid { get; init; }

    /// <summary>
    /// Identity sid used for the application. It must be of IdentityType 'sender_id_customer_profile'
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identity_sid")]
    [StringLength(34, MinimumLength = 34)]
    public string? IdentitySid { get; init; }

    /// <summary>
    /// Address sid used for the application
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_sid")]
    [StringLength(34, MinimumLength = 34)]
    public string? AddressSid { get; init; }

    /// <summary>
    /// Purpose for using Sender ID
    /// </summary>
    [JsonPropertyName("purpose")]
    public required SenderIdPurpose Purpose { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
