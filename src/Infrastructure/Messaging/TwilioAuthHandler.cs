using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioAuthHandler : DelegatingHandler
{
    private readonly IOptions<TwilioOptions> _options;

    public TwilioAuthHandler(IOptions<TwilioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var sid = _options.Value.AccountSid;
        var token = _options.Value.AuthToken;
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        return base.SendAsync(request, cancellationToken);
    }
}
