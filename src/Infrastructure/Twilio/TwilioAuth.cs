using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Twilio;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

internal static class TwilioAuth
{
    public static AuthenticationHeaderValue CreateBasicHeader(TwilioOptions options)
    {
        var raw = $"{options.AccountSid}:{options.AuthToken}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    public static TwilioOptions RequireConfigured(IOptions<TwilioOptions> optionsAccessor)
    {
        var options = optionsAccessor.Value;
        if (string.IsNullOrWhiteSpace(options.AccountSid)
            || string.IsNullOrWhiteSpace(options.AuthToken)
            || string.IsNullOrWhiteSpace(options.FromNumber))
        {
            throw new InvalidOperationException("Twilio:AccountSid, Twilio:AuthToken and Twilio:FromNumber must be configured.");
        }

        return options;
    }
}
