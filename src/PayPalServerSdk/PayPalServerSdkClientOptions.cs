using System;
using System.Collections.Generic;
using PayPalServerSdk.Core.Authentication.OAuth2;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.Configuration;
using PayPalServerSdk.Core.Hooks;
using PayPalServerSdk.Servers;

namespace PayPalServerSdk;

public class PayPalServerSdkClientOptions
{
    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Default();
    public RetryOptions Retry { get; set; } = RetryOptions.Default();
    public LoggingOptions Logging { get; set; } = new();
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
    public ServerOptions Server { get; set; } = new();
    /// <summary>
    /// Maximum time to wait for the next frame of a streaming (SSE) response before the stream is
    /// torn down with a timeout. Bounds only the wait for the server between frames, never the
    /// caller's own processing time. Set to null to wait indefinitely.
    /// </summary>
    public TimeSpan? StreamReadTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public IReadOnlyList<SdkHook> Hooks { get; set; } = [];
    /// <summary>
    /// Oauth 2.0 authentication, OAuth 2.0 authentication, Oauth 2.0 authentication, Oauth 2.0 authentication, Oauth 2.0 authentication
    /// </summary>
    public OAuth2ClientCredentials? Oauth2 { get; set; }
    public IOAuth2TokenStrategy<OAuth2ClientCredentials>? Oauth2TokenStrategy { get; set; }
}
