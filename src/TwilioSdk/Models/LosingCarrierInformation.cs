using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Core.Validation;
using TwilioSdk.Core.Validation.Attributes;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record LosingCarrierInformation
{
    /// <summary>
    /// Customer name as it is registered with the losing carrier. This can be an individual or a business name depending on the customer type selected.
    /// </summary>
    [JsonPropertyName("customer_name")]
    public required string CustomerName { get; init; }

    /// <summary>
    /// The account number of the customer for the losing carrier. Only require for mobile phone numbers.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_number")]
    public string? AccountNumber { get; init; }

    /// <summary>
    /// The account phone number of the customer for the losing carrier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_telephone_number")]
    public string? AccountTelephoneNumber { get; init; }

    /// <summary>
    /// If you already have an Address SID that represents the address needed for the LOA, you can provide an Address SID instead of providing the address object in the request body. This will copy the address into the port in request. If changes are made to the Address SID after port in request creation, those changes will not be reflected in the port in request.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_sid")]
    public string? AddressSid { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public Address? Address { get; init; }

    /// <summary>
    /// The first and last name of the person listed with the losing carrier who is authorized to make changes on the account.
    /// </summary>
    [JsonPropertyName("authorized_representative")]
    public required string AuthorizedRepresentative { get; init; }

    /// <summary>
    /// Email address of the person (owner of the number) who will sign the letter of authorization for the port in request. This email address should belong to the person named in as the authorized representative.
    /// </summary>
    [JsonPropertyName("authorized_representative_email")]
    [Format(FormatKind.Email)]
    public required string AuthorizedRepresentativeEmail { get; init; }

    /// <summary>
    /// The type of customer account in the losing carrier. This should either be: 'Individual' or 'Business'.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("customer_type")]
    public CustomerType? CustomerType { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("authorized_representative_katakana")]
    public string? AuthorizedRepresentativeKatakana { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sub_municipality")]
    public string? SubMunicipality { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("building")]
    public string? Building { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("katakana_name")]
    public string? KatakanaName { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
