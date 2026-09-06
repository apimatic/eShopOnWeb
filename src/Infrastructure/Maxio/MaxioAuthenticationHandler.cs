using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Applies the <c>BasicAuth</c> security scheme declared in maxio-spec/openapi.yaml: the API key is
/// the HTTP Basic username and the password is the literal <c>x</c>.
/// </summary>
/// <remarks>
/// The header is attached here, per request, rather than on the shared <see cref="HttpClient"/>, so
/// that a rotated key from <see cref="IOptionsMonitor{TOptions}"/> takes effect without a restart
/// and so the credential is never captured in a long-lived client instance.
/// </remarks>
public class MaxioAuthenticationHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<MaxioSettings> _settings;

    public MaxioAuthenticationHandler(IOptionsMonitor<MaxioSettings> settings)
    {
        _settings = settings;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var apiKey = _settings.CurrentValue.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Maxio:ApiKey is not configured. Set it with dotnet user-secrets, or supply MAXIO_API_KEY in the environment.");
        }

        var credential = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{apiKey}:x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credential);
        request.Headers.Accept.ParseAdd("application/json");

        return base.SendAsync(request, cancellationToken);
    }
}
