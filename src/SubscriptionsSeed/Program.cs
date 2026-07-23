using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;

// UC0 operator tooling. Provisions (or just verifies) the billing entities the subscription
// module expects. Deliberately NOT wired into the Web or PublicApi hosts: it is run by a developer
// setting up a sandbox, never by a customer.
//
//   dotnet run --project src/SubscriptionsSeed            -> verify only, reports what is missing
//   dotnet run --project src/SubscriptionsSeed -- --seed  -> create whatever is missing, then verify

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<SeedRunner>(optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args)
    .Build();

var applyChanges = args.Any(a =>
    string.Equals(a, "--seed", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(a, "--apply", StringComparison.OrdinalIgnoreCase));

try
{
    var runner = new SeedRunner(configuration);
    return await runner.RunAsync(applyChanges, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAILED: {ex.Message}");
    return 1;
}
