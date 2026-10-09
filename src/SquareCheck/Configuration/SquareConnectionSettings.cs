using Square.Servers;

namespace Microsoft.eShopWeb.SquareCheck.Configuration;

/// <summary>Validated settings the tool runs with. Only <see cref="SquareSettingsValidator"/> creates one.</summary>
public sealed class SquareConnectionSettings
{
    internal SquareConnectionSettings(
        string environmentName,
        ServerEnvironment environment,
        string applicationId,
        string applicationSecret,
        string redirectUri)
    {
        EnvironmentName = environmentName;
        Environment = environment;
        ApplicationId = applicationId;
        ApplicationSecret = applicationSecret;
        RedirectUri = redirectUri;
        RedirectAddress = new Uri(redirectUri, UriKind.Absolute);
    }

    /// <summary><c>sandbox</c> or <c>production</c>.</summary>
    public string EnvironmentName { get; }
    public ServerEnvironment Environment { get; }
    public string ApplicationId { get; }
    public string ApplicationSecret { get; }

    /// <summary>The redirect URL exactly as registered with Square; sent verbatim in both sign-in requests.</summary>
    public string RedirectUri { get; }

    /// <summary>The parsed <see cref="RedirectUri"/>, used to listen for Square's redirect.</summary>
    public Uri RedirectAddress { get; }

    public override string ToString() => $"{nameof(SquareConnectionSettings)} {{ Environment = {EnvironmentName}, values redacted }}";
}
