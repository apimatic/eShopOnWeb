using System.Text.Json.Serialization;
using TwilioSdk.Core.Models;

namespace TwilioSdk.Models;

/// <summary>
/// The result of a WebAuthn authentication via a <c>navigator.credentials.get()</c> request, as specified in <see href="https://developer.mozilla.org/en-US/docs/Web/API/AuthenticatorAttestationResponse">AuthenticatorAttestationResponse</see>.
/// </summary>
public record Response1
{
    /// <summary>
    /// The <see href="https://developer.mozilla.org/en-US/docs/Web/API/Web_Authentication_API/Authenticator_data">authenticator data</see> structure contains information from the authenticator about the processing of a credential creation or authentication request.
    /// </summary>
    [JsonPropertyName("authenticatorData")]
    public required string AuthenticatorData { get; init; }

    /// <summary>
    /// This property contains the JSON-compatible serialization of the data passed from the browser to the authenticator in order to generate this credential.
    /// </summary>
    [JsonPropertyName("clientDataJSON")]
    public required string ClientDataJson { get; init; }

    /// <summary>
    /// An assertion signature over <c>authenticatorData</c> and <c>clientDataJSON</c>. The assertion signature is created with the private key of the key pair that was created during the originating <c>navigator.credentials.create()</c> call and verified using the public key of that same key pair.
    /// </summary>
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }

    /// <summary>
    /// The user handle stored in the authenticator, specified as <c>user.id</c> in the options passed to the originating <c>navigator.credentials.create()</c> call. This property should contain a base64url-encoded entity SID.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("userHandle")]
    public string? UserHandle { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
