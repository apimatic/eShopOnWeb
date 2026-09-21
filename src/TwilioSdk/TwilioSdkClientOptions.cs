using System.Collections.Generic;
using TwilioSdk.Core.Authentication.Basic;
using TwilioSdk.Core.Configuration;
using TwilioSdk.Core.Hooks;
using TwilioSdk.Servers;

namespace TwilioSdk;

public class TwilioSdkClientOptions
{
    public ServerEnvironment Environment { get; set; } = ServerEnvironment.Default();
    public RetryOptions Retry { get; set; } = RetryOptions.Default();
    public LoggingOptions Logging { get; set; } = new();
    public ServerOptions Server { get; set; } = new();
    public IReadOnlyList<SdkHook> Hooks { get; set; } = [];
    /// <summary>
    /// This API uses <see href="https://www.twilio.com/docs/glossary/what-is-basic-authentication">basic authentication</see>. Use an <see href="https://www.twilio.com/docs/iam/api-keys">API key</see> as the username and the API key secret as the password. You can also use your account SID and auth token, but limit their use to local testing.
    /// </summary>
    public BasicAuthCredentials? AccountSidAuthToken { get; set; }
}
