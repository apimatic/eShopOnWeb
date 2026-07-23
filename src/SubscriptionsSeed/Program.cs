using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.SubscriptionsSeed;

// UC0 operator tooling: provisions and verifies the billing entities the subscription integration
// expects. It is deliberately NOT wired into either host — a customer never runs this.
//
//   dotnet run --project src/SubscriptionsSeed                        # provision anything missing, then verify
//   dotnet run --project src/SubscriptionsSeed -- --verify-only       # verify only, change nothing
//   dotnet run --project src/SubscriptionsSeed -- --recreate-component # archive a mis-typed component and recreate it
//
// Credentials come from user-secrets or environment variables; nothing sensitive is read from source.

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var options = SeedOptions.Parse(args);
    var runner = new SeedRunner(SeedConfiguration.Load(), options);
    return await runner.RunAsync(cancellation.Token);
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Cancelled.");
    return 2;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Seeding failed: {ex.Message}");
    return 1;
}
