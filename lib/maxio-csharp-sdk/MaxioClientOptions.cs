using System.Collections.Generic;
using Maxio.Core.Authentication.Basic;
using Maxio.Core.Configuration;
using Maxio.Core.Hooks;
using Maxio.Servers;

namespace Maxio;

public class MaxioClientOptions
{
    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Default();
    public RetryOptions Retry { get; set; } = RetryOptions.Default();
    public LoggingOptions Logging { get; set; } = new();
    public ServerOptions Server { get; set; } = new();
    public IReadOnlyList<SdkHook> Hooks { get; set; } = [];
    /// <summary>
    /// The <c>username</c> is a Maxio Chargify API key and the <c>password</c> is <c>x</c>. Basic authentication works only with the US and EU environments, which connect to <c>chargify.com</c> directly. The Maxio API Gateway environment does not accept Basic authentication.
    /// </summary>
    public BasicAuthCredentials? BasicAuth { get; set; }
    /// <summary>
    /// A Maxio API Gateway connector token — the only authentication the gateway accepts. Use it with the Maxio API Gateway environment. This token is issued by your connector and is separate from your Chargify API key. Depending on how the connector was created, it is either a static connector API token you copy from your connector settings (long-lived, valid until you rotate it) or an access token you obtain by exchanging OAuth2 client credentials at <c>https://&lt;connector&gt;.api.maxio.com/oauth/token</c>.
    /// </summary>
    public string? BearerAuth { get; set; }
}
