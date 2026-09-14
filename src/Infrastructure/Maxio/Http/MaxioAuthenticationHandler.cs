using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Http;

/// <summary>
/// Applies the <c>BasicAuth</c> security scheme the Maxio specification defines: the username is
/// the API key and the password is the literal <c>x</c>.
/// </summary>
/// <remarks>
/// The header is built per request from <see cref="IOptionsMonitor{TOptions}"/> rather than baked
/// into the client at construction, so a rotated key takes effect on the next call instead of on
/// the next restart. The key itself never appears in a log or an exception message.
/// </remarks>
public class MaxioAuthenticationHandler : DelegatingHandler
{
    private const string BasicAuthPassword = "x";

    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BillingConfigurationException(
                $"'{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}' is not configured.");
        }

        var credential = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{apiKey}:{BasicAuthPassword}"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);

        return base.SendAsync(request, cancellationToken);
    }
}
