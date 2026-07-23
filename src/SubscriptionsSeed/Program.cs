using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// UC0 operator tooling: verifies that the billing provider sandbox holds the entities this
// integration is configured to use. It is intentionally NOT wired into either host — a developer
// runs it by hand after seeding or re-seeding a sandbox.
//
//   dotnet run --project src/SubscriptionsSeed
//
// Exits 0 when the seed is good and 1 when it is not, so it can gate a setup script.

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddUserSecrets<SeedVerifier>(optional: true);
builder.Configuration.AddEnvironmentVariables();

builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.Services.AddBillingServices(builder.Configuration);
builder.Services.AddSingleton<SeedVerifier>();

using var host = builder.Build();

var settings = host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<MaxioSettings>>().Value;
var billingClient = host.Services.GetRequiredService<IBillingClient>();
var verifier = host.Services.GetRequiredService<SeedVerifier>();

return await verifier.VerifyAsync(billingClient, settings);
