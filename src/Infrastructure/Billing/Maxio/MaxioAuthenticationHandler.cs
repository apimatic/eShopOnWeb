using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Applies the specification's <c>BasicAuth</c> security scheme to every Maxio request: the API key
/// is the user name and the password is the literal <c>x</c>. The key is read from configuration on
/// each request so a rotated secret takes effect without a restart, and it is never logged.
/// </summary>
public class MaxioAuthenticationHandler : DelegatingHandler
{
    private const string PasswordPlaceholder = "x";

    private readonly IOptionsMonitor<MaxioOptions> _options;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioOptions> options)
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var apiKey = _options.CurrentValue.ApiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BillingConfigurationException(
                $"Maxio subscription billing is not configured: '{MaxioOptions.SectionName}:{nameof(MaxioOptions.ApiKey)}' is not set.");
        }

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{apiKey}:{PasswordPlaceholder}"));

        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        return base.SendAsync(request, cancellationToken);
    }
}
