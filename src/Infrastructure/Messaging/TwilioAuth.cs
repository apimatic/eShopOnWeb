using System;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioAuth
{
    public static AuthenticationHeaderValue CreateBasicHeader(TwilioSettings settings)
    {
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", credentials);
    }

    public static AuthenticationHeaderValue CreateBasicHeader(IOptions<TwilioSettings> options)
        => CreateBasicHeader(options.Value);
}
