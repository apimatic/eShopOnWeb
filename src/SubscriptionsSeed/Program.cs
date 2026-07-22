using MaxioAdvancedBilling;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;

// UC0 operator tooling (plan.md §6 Phase 0): provisions and verifies the product family, the two
// recurring plans, and the metered component the integration is configured against. It is not wired
// into either host and is never reachable from the storefront.
//
//   dotnet run --project src/SubscriptionsSeed             verify, and create anything missing
//   dotnet run --project src/SubscriptionsSeed -- --repair  additionally archive and recreate a
//                                                          component of the wrong kind or family
//
// Configuration is read from user-secrets and the environment; no credential is ever read from, or
// written to, a file in the repository.

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(SeedCatalog).Assembly, optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var settings = configuration.GetSection(MaxioSettings.SectionName).Get<MaxioSettings>() ?? new MaxioSettings();
var repair = args.Contains("--repair", StringComparer.OrdinalIgnoreCase);

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    settings.Validate();
}
catch (BillingConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine("Set the Maxio settings first, for example:");
    Console.Error.WriteLine("  dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\" --project src/SubscriptionsSeed");
    return 2;
}

// One HttpClient for the life of the process: the SDK does not own it, and a client per call would
// exhaust sockets.
using var httpClient = new HttpClient();
var maxio = new MaxioAdvancedBillingClient(httpClient, MaxioBillingDependencies.CreateClientOptions(settings));

var seeder = new MaxioSandboxSeeder(maxio, settings, repair);

try
{
    var succeeded = await seeder.RunAsync(cancellation.Token);

    Console.WriteLine();
    if (succeeded)
    {
        Console.WriteLine("Seed complete: the sandbox matches the integration's configuration.");
        return 0;
    }

    Console.Error.WriteLine("Seed finished with problems:");
    foreach (var problem in seeder.Problems)
    {
        Console.Error.WriteLine($"  - {problem}");
    }

    return 1;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Seed failed: {ex.Message}");
    return 1;
}
