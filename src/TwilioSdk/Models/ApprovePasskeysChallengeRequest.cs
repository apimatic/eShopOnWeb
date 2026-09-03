using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;
using TwilioSdk.Models.Enums;

namespace TwilioSdk.Models;

public record ApprovePasskeysChallengeRequest
{
    /// <summary>
    /// A <see href="https://base64.guru/standards/base64url">base64url</see> encoded representation of <c>rawId</c>.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// The globally unique identifier for this <c>PublicKeyCredential</c>.
    /// </summary>
    [JsonPropertyName("rawId")]
    public required string RawId { get; init; }

    /// <summary>
    /// A string that indicates the mechanism by which the WebAuthn implementation is attached to the authenticator at the time the associated <c>navigator.credentials.create()</c> or <c>navigator.credentials.get()</c> call completes.
    /// </summary>
    [JsonPropertyName("authenticatorAttachment")]
    public required AuthenticatorAttachment2 AuthenticatorAttachment { get; init; }

    /// <summary>
    /// The valid credential types supported by the API. The values of this enumeration are used for versioning the <c>AuthenticatorAssertion</c> and <c>AuthenticatorAttestation</c> structures according to the type of the authenticator.
    /// </summary>
    [JsonPropertyName("type")]
    public Type1? Type { get; init; } = Type1.PublicKey;

    /// <summary>
    /// The result of a WebAuthn authentication via a <c>navigator.credentials.get()</c> request, as specified in <see href="https://developer.mozilla.org/en-US/docs/Web/API/AuthenticatorAttestationResponse">AuthenticatorAttestationResponse</see>.
    /// </summary>
    [JsonPropertyName("response")]
    public required Response1 Response { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
