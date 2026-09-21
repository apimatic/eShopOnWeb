using System.Text.Json.Serialization;
using PayPalServerSdk.Core.Models;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The request-related <see href="https://developer.paypal.com/api/rest/responses/#hateoas-links">HATEOAS link</see> information., The request-related <see href="/api/rest/responses/#hateoas-links">HATEOAS link</see> information., The request-related <see href="https://developer.paypal.com/api/rest/responses/#hateoas-links">HATEOAS link</see> information.
/// </summary>
public record LinkDescription
{
    /// <summary>
    /// The complete target URL. To make the related call, combine the method with this <see href="https://tools.ietf.org/html/rfc6570">URI Template-formatted</see> link. For pre-processing, include the <c>$</c>, <c>(</c>, and <c>)</c> characters. The <c>href</c> is the key HATEOAS component that links a completed call with a subsequent call.
    /// </summary>
    [JsonPropertyName("href")]
    public string? Href { get; init; }

    /// <summary>
    /// The <see href="https://tools.ietf.org/html/rfc5988#section-4">link relation type</see>, which serves as an ID for a link that unambiguously describes the semantics of the link. See <see href="https://www.iana.org/assignments/link-relations/link-relations.xhtml">Link Relations</see>.
    /// </summary>
    // NOTE (eShopOnWeb): relaxed from `required` to optional. The generated model marked `href`/`rel`
    // required, but PayPal's live Vault (payment-token) success response returns a HATEOAS link object
    // that omits `rel`, which made the SDK throw System.Text.Json.JsonException on an otherwise-successful
    // 200 response and blocked the saved-card flow. Orders/Payments responses always include `rel`, so
    // this only affects Vault. Relaxing to nullable matches the observed API contract; the integration
    // does not consume these links. This is a corrected generated-SDK/spec drift, documented in
    // pay-pal-server-sdk-plan.md.
    [JsonPropertyName("rel")]
    public string? Rel { get; init; }

    /// <summary>
    /// The HTTP method required to make the related call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public LinkHttpMethod? Method { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
