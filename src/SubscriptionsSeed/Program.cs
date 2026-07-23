using System.Net.Http.Headers;
using System.Text;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// UC0 operator tooling. Run once per sandbox (or after a reset) to provision the product family,
/// the two recurring plans, and the metered component the subscription module expects.
/// </summary>
/// <remarks>
/// This is not wired into the storefront or the PublicApi and is never run by a customer.
/// Configuration is read from the same "Maxio" section the hosts use, supplied through this
/// project's own user-secrets or <c>Maxio__*</c> environment variables. Pass <c>--verify-only</c>
/// to inspect the sandbox without changing anything.
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var verifyOnly = args.Any(a => string.Equals(a, "--verify-only", StringComparison.OrdinalIgnoreCase));

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();

        try
        {
            if (string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                await Console.Error.WriteLineAsync(
                    "Maxio:ApiKey is not configured. Set it with 'dotnet user-secrets set \"Maxio:ApiKey\" <key> --project src/SubscriptionsSeed' or the Maxio__ApiKey environment variable.");
                return 2;
            }

            var specification = SeedSpecification.FromSettings(settings);
            var baseUrl = settings.ResolveBaseUrl();

            Console.WriteLine($"Target : {baseUrl}");
            Console.WriteLine($"Family : {specification.FamilyHandle}");
            Console.WriteLine($"Mode   : {(verifyOnly ? "verify only (no changes)" : "provision missing entities")}");
            Console.WriteLine();

            using var httpClient = CreateHttpClient(settings, baseUrl);
            var seeder = new MaxioSeeder(httpClient, Console.Out);

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            var satisfied = await seeder.RunAsync(specification, verifyOnly, cancellation.Token);

            Console.WriteLine();
            Console.WriteLine(satisfied
                ? "Seed satisfied: the sandbox matches what the integration expects."
                : "Seed NOT satisfied: correct the reported entities and re-run.");

            return satisfied ? 0 : 1;
        }
        catch (BillingConfigurationException ex)
        {
            await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}");
            return 2;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            await Console.Error.WriteLineAsync($"Seed failed: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Seed cancelled.");
            return 1;
        }
    }

    private static HttpClient CreateHttpClient(MaxioSettings settings, Uri baseUrl)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds > 0 ? settings.TimeoutSeconds : 30)
        };

        // Maxio authenticates with HTTP Basic: the API key is the username, the password is "x".
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{settings.ApiKey}:x"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return httpClient;
    }
}
