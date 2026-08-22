using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Applies HTTP Basic authentication using AccountSid:AuthToken as specified by
/// the Twilio OpenAPI accountSid_authToken security scheme. Credentials are never logged.
/// </summary>
public class TwilioBasicAuthHandler : DelegatingHandler
{
    private readonly IOptions<TwilioSettings> _options;

    public TwilioBasicAuthHandler(IOptions<TwilioSettings> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var raw = $"{settings.AccountSid}:{settings.AuthToken}";
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        return base.SendAsync(request, cancellationToken);
    }
}
