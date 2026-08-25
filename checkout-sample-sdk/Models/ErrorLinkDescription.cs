using System.Text.Json.Serialization;
using PayPalServerSdk.Models.Enums;

namespace PayPalServerSdk.Models;

/// <summary>
/// The error-related <see href="/api/rest/responses/#hateoas-links">HATEOAS link</see> information. Identical to <c>link_description</c> except that <c>rel</c> is optional: the live API omits <c>rel</c> on the documentation link it returns with <c>RESOURCE_NOT_FOUND</c> errors, so a client that requires it cannot read the error at all.
/// </summary>
public record ErrorLinkDescription
{
    /// <summary>
    /// The complete target URL. To make the related call, combine the method with this <see href="https://tools.ietf.org/html/rfc6570">URI Template-formatted</see> link. For pre-processing, include the <c>$</c>, <c>(</c>, and <c>)</c> characters. The <c>href</c> is the key HATEOAS component that links a completed call with a subsequent call.
    /// </summary>
    [JsonPropertyName("href")]
    public required string Href { get; init; }

    /// <summary>
    /// The <see href="https://tools.ietf.org/html/rfc5988#section-4">link relation type</see>, which serves as an ID for a link that unambiguously describes the semantics of the link. See <see href="https://www.iana.org/assignments/link-relations/link-relations.xhtml">Link Relations</see>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rel")]
    public string? Rel { get; init; }

    /// <summary>
    /// The HTTP method required to make the related call.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("method")]
    public LinkHttpMethod? Method { get; init; }
}
