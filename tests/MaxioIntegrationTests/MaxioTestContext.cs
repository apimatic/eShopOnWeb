using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.MaxioIntegrationTests;

/// <summary>Captures log output so tests can assert the client reports what it swallows.</summary>
public class RecordingAppLogger<T> : IAppLogger<T>
{
    public List<string> Information { get; } = new();
    public List<string> Warnings { get; } = new();

    public void LogInformation(string message, params object[] args) => Information.Add(Format(message, args));

    public void LogWarning(string message, params object[] args) => Warnings.Add(Format(message, args));

    private static string Format(string message, object[] args)
    {
        try
        {
            return args.Length == 0 ? message : string.Format(message, args);
        }
        catch (FormatException)
        {
            return message;
        }
    }
}

/// <summary>
/// Builds a <see cref="MaxioBillingClient"/> wired to a <see cref="FakeMaxioServer"/> exactly the way
/// the composition root wires the real one — same settings type, same base-URL resolution, same
/// singleton validation cache.
/// </summary>
public class MaxioTestContext
{
    public const string ProductFamilyHandle = "eshop-subscribe";
    public const int ProductFamilyId = 3026730;
    public const string MeteredComponentHandle = "api-call";

    public MaxioTestContext(MaxioSettings? settings = null)
    {
        Server = new FakeMaxioServer();

        Settings = settings ?? new MaxioSettings
        {
            ApiKey = "test-api-key",
            Subdomain = "cp-exp-3",
            Environment = "US",
            ProductFamilyHandle = ProductFamilyHandle,
            ProductFamilyId = ProductFamilyId,
            DefaultProductHandle = "eshop-pro",
            AlternateProductHandle = "basic-plan",
            MeteredComponentHandle = MeteredComponentHandle
        };

        Logger = new RecordingAppLogger<MaxioBillingClient>();
        ValidationCache = new MaxioComponentValidationCache();

        HttpClient = new HttpClient(Server)
        {
            BaseAddress = new Uri(Settings.ResolveBaseUrl())
        };

        Client = new MaxioBillingClient(HttpClient, Options.Create(Settings), ValidationCache, Logger);
    }

    public FakeMaxioServer Server { get; }
    public MaxioSettings Settings { get; }
    public HttpClient HttpClient { get; }
    public RecordingAppLogger<MaxioBillingClient> Logger { get; }
    public MaxioComponentValidationCache ValidationCache { get; }
    public MaxioBillingClient Client { get; }

    /// <summary>The family listing route the client resolves plans through.</summary>
    public static string PlansRoute => $"product_families/handle:{ProductFamilyHandle}/products.json";

    public static string FamilyRoute => $"product_families/handle:{ProductFamilyHandle}.json";

    public static string ComponentRoute =>
        $"product_families/{ProductFamilyId}/components/handle:{MeteredComponentHandle}.json";

    public static string CustomerLookupRoute(string reference) =>
        $"customers/lookup.json?reference={Uri.EscapeDataString(reference)}";
}
