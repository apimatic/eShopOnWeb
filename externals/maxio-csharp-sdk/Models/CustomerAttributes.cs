using System.Collections.Generic;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CustomerAttributes
{
    /// <summary>
    /// The first name of the customer. Required when creating a customer via attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("first_name")]
    public string? FirstName { get; init; }

    /// <summary>
    /// The last name of the customer. Required when creating a customer via attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("last_name")]
    public string? LastName { get; init; }

    /// <summary>
    /// The email address of the customer. Required when creating a customer via attributes.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("email")]
    public string? Email { get; init; }

    /// <summary>
    /// (Optional) A list of emails that should be cc’d on all customer communications.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("cc_emails")]
    public string? CcEmails { get; init; }

    /// <summary>
    /// (Optional) The organization/company of the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("organization")]
    public string? Organization { get; init; }

    /// <summary>
    /// (Optional) A customer “reference”, or unique identifier from your app, stored in Chargify. Can be used so that you may reference your customer’s within Chargify using the same unique value you use in your application.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    /// <summary>
    /// (Optional) The customer’s shipping street address (e.g., “123 Main St.”).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>
    /// (Optional) Second line of the customer’s shipping address e.g., “Apt. 100”
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("address_2")]
    public string? Address2 { get; init; }

    /// <summary>
    /// (Optional) The customer’s shipping address city (e.g., “Boston”).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("city")]
    public string? City { get; init; }

    /// <summary>
    /// “(Optional) The customer’s shipping address state (e.g., “MA”). This must conform to the <see href="https://en.wikipedia.org/wiki/ISO_3166-1#Current_codes">ISO_3166-1</see> in order to be valid for tax locale purposes.”
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>
    /// (Optional) The customer’s shipping address zip code (e.g., “12345”).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("zip")]
    public string? Zip { get; init; }

    /// <summary>
    /// “(Optional) The customer shipping address country, required in <see href="https://en.wikipedia.org/wiki/ISO_3166-1_alpha-2">ISO_3166-1 alpha-2</see> format (e.g., “US”).”
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("country")]
    public string? Country { get; init; }

    /// <summary>
    /// (Optional) The phone number of the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("verified")]
    public bool? Verified { get; init; }

    /// <summary>
    /// (Optional) The tax_exempt status of the customer. Acceptable values are true or 1 for true and false or 0 for false.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("tax_exempt")]
    public bool? TaxExempt { get; init; }

    /// <summary>
    /// (Optional) Whether surcharging is enabled for the customer. Defaults to <c>true</c> when omitted. Only applied on sites where surcharging control is enabled.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("surcharging")]
    public bool? Surcharging { get; init; }

    /// <summary>
    /// (Optional) Supplying the VAT number allows EU customers to opt-out of the Value Added Tax assuming the merchant address and customer billing address are not within the same EU country. It’s important to omit the country code from the VAT number upon entry. Otherwise, taxes will be assessed upon the purchase.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("vat_number")]
    public string? VatNumber { get; init; }

    /// <summary>
    /// (Optional) A set of key/value pairs representing custom fields and their values. Metafields will be created “on-the-fly” in your site for a given key, if they have not been created yet.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("metafields")]
    public IReadOnlyDictionary<string, string>? Metafields { get; init; }

    /// <summary>
    /// The parent ID in Chargify if applicable. Parent is another Customer object.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("parent_id")]
    public int? ParentId { get; init; }

    /// <summary>
    /// (Optional) The Salesforce ID of the customer.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("salesforce_id")]
    public string? SalesforceId { get; init; }

    /// <summary>
    /// (Optional) The default auto-renewal profile ID for the customer
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("default_auto_renewal_profile_id")]
    public int? DefaultAutoRenewalProfileId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
