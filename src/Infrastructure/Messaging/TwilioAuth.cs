using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

internal static class TwilioAuth
{
    public static AuthenticationHeaderValue CreateBasicHeader(TwilioSettings settings)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.AccountSid}:{settings.AuthToken}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static void EnsureConfigured(TwilioSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AccountSid) || string.IsNullOrWhiteSpace(settings.AuthToken))
        {
            throw new InvalidOperationException("Twilio AccountSid and AuthToken must be configured.");
        }
    }

    public static Uri ResolveAgainstBase(Uri baseAddress, string uriOrPath)
    {
        if (Uri.TryCreate(uriOrPath, UriKind.Absolute, out var absolute))
        {
            var relative = absolute.PathAndQuery.TrimStart('/');
            return new Uri(baseAddress, relative);
        }

        return new Uri(baseAddress, uriOrPath.TrimStart('/'));
    }
}
