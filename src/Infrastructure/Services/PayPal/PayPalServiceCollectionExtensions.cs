using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Wires the PayPal integration into the application's service container: binds <see cref="PayPalSettings"/>,
/// constructs the SDK client over an <see cref="IHttpClientFactory"/>-managed HttpClient, and registers the
/// gateway. PublicApi owns endpoint/Program.cs wiring; this only registers services.
/// </summary>
public static class PayPalServiceCollectionExtensions
{
    /// <summary>The named HttpClient the SDK client is built over — keeps its pipeline off the shared default client.</summary>
    private const string HttpClientName = "PayPal";

    public static IServiceCollection AddPayPalIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PayPalSettings>(configuration.GetSection(PayPalSettings.SectionName));

        // Named HttpClient: a bounded per-attempt timeout, a rotating primary handler (the SDK client below is a
        // long-lived singleton, so PooledConnectionLifetime is what keeps DNS fresh), and a handler that records
        // the last HTTP status so the gateway can tell a deterministic rejection from an outage when the SDK
        // throws a JsonException while (de)serializing a body.
        services.AddHttpClient(HttpClientName, c =>
            {
                c.Timeout = TimeSpan.FromSeconds(30); // bounds ONE attempt (not the whole call) — a hang ends here
            })
            .AddHttpMessageHandler(() => new StatusCapturingHandler())
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5)
            });

        // The SDK client is lightweight controller wrappers over the shared HTTP pipeline and is meant to be
        // long-lived: build it once as a singleton. Options (env / base URL / credentials) are set BEFORE
        // construction, per the client-initialization skill.
        services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<PayPalSettings>>().Value;
            var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            var options = new PayPalServerSdkClientOptions
            {
                // The SDK exposes only the Sandbox environment; live is reached purely by overriding the base URL.
                Environment = ServerEnvironment.Sandbox,
                Oauth2 = new OAuth2ClientCredentials
                {
                    ClientId = settings.ClientId,
                    ClientSecret = settings.ClientSecret
                }
            };

            // Base-URL resolution. The token request and every API call resolve through this same
            // Server.Default.Sandbox.BaseUrl, so one value covers both.
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
            {
                options.Server.Default.Sandbox.BaseUrl = settings.BaseUrl!; // verbatim override
            }
            else if (IsLive(settings.Environment))
            {
                options.Server.Default.Sandbox.BaseUrl = "https://api-m.paypal.com";
            }
            // else: leave the SDK's sandbox default (https://api-m.sandbox.paypal.com)

            return new PayPalServerSdkClient(httpClient, options);
        });

        services.AddScoped<IPayPalGateway, PayPalGateway>();

        return services;
    }

    private static bool IsLive(string? environment) =>
        !string.IsNullOrWhiteSpace(environment) &&
        (environment!.Equals("live", StringComparison.OrdinalIgnoreCase) ||
         environment.Equals("production", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Flows a mutable per-call scope down to <see cref="StatusCapturingHandler"/> so the gateway can read the
/// HTTP status of the last response even when the SDK throws (typed errors do not carry the status, and a
/// JsonException on the error path destroys the SdkException that would have). The gateway opens a scope with
/// <see cref="Begin"/>; the handler (running inside the awaited call) mutates the SAME object, so the value is
/// visible back in the gateway — the pattern the resilience skill prescribes for state that must survive across
/// the retry pipeline.
/// </summary>
internal sealed class PayPalCallScope : IDisposable
{
    private static readonly AsyncLocal<PayPalCallScope?> _current = new();

    public int? StatusCode { get; set; }

    public static PayPalCallScope? Current => _current.Value;

    public static PayPalCallScope Begin()
    {
        var scope = new PayPalCallScope();
        _current.Value = scope;
        return scope;
    }

    public void Dispose() => _current.Value = null;
}

/// <summary>Records the status code of the last response into the ambient <see cref="PayPalCallScope"/>.</summary>
internal sealed class StatusCapturingHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var scope = PayPalCallScope.Current;
        if (scope is not null)
        {
            scope.StatusCode = (int)response.StatusCode;
        }
        return response;
    }
}
