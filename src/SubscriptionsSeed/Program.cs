using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.eShopWeb.SubscriptionsSeed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

// UC0 — provisions the product family, the two recurring plans, and the metered component the
// subscription integration expects. Operator tooling: it is never reachable from the storefront or
// the public API, and is safe to re-run — existing entities are verified rather than duplicated.
//
//   dotnet run --project src/SubscriptionsSeed                 seed and verify
//   dotnet run --project src/SubscriptionsSeed -- --verify     verify only, change nothing

var verifyOnly = args.Any(a =>
    a.Equals("--verify", StringComparison.OrdinalIgnoreCase) ||
    a.Equals("--verify-only", StringComparison.OrdinalIgnoreCase));

var configuration = new ConfigurationBuilder()
    .AddUserSecrets(typeof(SeedDefinition).Assembly, optional: true)
    .AddEnvironmentVariables()
    .Build();

var settings = configuration.GetSection(MaxioSettings.ConfigurationSectionName).Get<MaxioSettings>()
    ?? new MaxioSettings();

if (string.IsNullOrWhiteSpace(settings.ApiKey))
{
    Console.Error.WriteLine(
        "Maxio:ApiKey is not configured. Set it with:\n" +
        "  dotnet user-secrets set \"Maxio:ApiKey\" \"<key>\" -p src/SubscriptionsSeed\n" +
        "or export Maxio__ApiKey in the environment.");
    return 2;
}

var definition = new SeedDefinition(
    Fallback(settings.ProductFamilyHandle, "eshop-subscribe"),
    Fallback(settings.DefaultProductHandle, "eshop-pro"),
    Fallback(settings.AlternateProductHandle, "basic-plan"),
    Fallback(settings.MeteredComponentHandle, "api-call"));

using var httpClient = new HttpClient();
var client = new MaxioBillingClient(httpClient, Options.Create(settings));

Console.WriteLine($"Target server : {client.TargetBaseUrl}");
Console.WriteLine($"Mode          : {(verifyOnly ? "verify only" : "seed and verify")}");
Console.WriteLine();

try
{
    return await RunAsync(client, definition, verifyOnly, CancellationToken.None);
}
catch (BillingConfigurationException ex)
{
    Console.Error.WriteLine($"Configuration problem: {ex.Message}");
    return 2;
}
catch (BillingProviderException ex)
{
    Console.Error.WriteLine($"The billing provider refused the request: {ex.Message}");
    return 1;
}

static async Task<int> RunAsync(IBillingProvisioningClient client,
    SeedDefinition definition,
    bool verifyOnly,
    CancellationToken cancellationToken)
{
    var problems = 0;

    // 1 — product family.
    var family = await client.FindProductFamilyByHandleAsync(definition.FamilyHandle, cancellationToken);
    if (family is null)
    {
        if (verifyOnly)
        {
            Console.WriteLine($"MISSING  product family '{definition.FamilyHandle}'");
            return 1;
        }

        family = await client.CreateProductFamilyAsync(definition.FamilyHandle, SeedDefinition.FamilyName,
            SeedDefinition.FamilyDescription, cancellationToken);
        Console.WriteLine($"CREATED  product family '{family.Handle}' (id {family.Id})");
    }
    else
    {
        Console.WriteLine($"EXISTS   product family '{family.Handle}' (id {family.Id})");
    }

    // 2 and 3 — the two recurring plans.
    problems += await EnsurePlanAsync(client, family, definition.DefaultPlanHandle, SeedDefinition.DefaultPlanName,
        SeedDefinition.DefaultPlanDescription, SeedDefinition.DefaultPlanPrice, verifyOnly, cancellationToken);

    problems += await EnsurePlanAsync(client, family, definition.AlternatePlanHandle, SeedDefinition.AlternatePlanName,
        SeedDefinition.AlternatePlanDescription, SeedDefinition.AlternatePlanPrice, verifyOnly, cancellationToken);

    // 4 — the metered component.
    problems += await EnsureComponentAsync(client, family, definition, verifyOnly, cancellationToken);

    // 5 and 6 — read everything back and print the identifiers the integration should be configured with.
    Console.WriteLine();
    Console.WriteLine("Verifying by reading the family back...");

    var plans = await client.ListPlansForFamilyAsync(family, false, cancellationToken);
    var components = await client.ListComponentsForFamilyAsync(family, false, cancellationToken);

    foreach (var handle in new[] { definition.DefaultPlanHandle, definition.AlternatePlanHandle })
    {
        var plan = plans.FirstOrDefault(p => Matches(p.Handle, handle));
        if (plan is null)
        {
            Console.WriteLine($"  FAIL   plan '{handle}' is not listed under the family");
            problems++;
        }
        else
        {
            Console.WriteLine($"  ok     plan '{plan.Handle}' id {plan.Id}, {plan.BillingDescription}, " +
                              $"payment method required: {plan.RequiresPaymentMethod}");
        }
    }

    var component = components.FirstOrDefault(c => Matches(c.Handle, definition.MeteredComponentHandle));
    if (component is null)
    {
        Console.WriteLine($"  FAIL   component '{definition.MeteredComponentHandle}' is not listed under the family");
        problems++;
    }
    else if (!component.IsMetered)
    {
        Console.WriteLine($"  FAIL   component '{component.Handle}' is {component.Kind}, not metered");
        problems++;
    }
    else
    {
        Console.WriteLine($"  ok     component '{component.Handle}' id {component.Id}, metered, " +
                          $"{component.UnitPrice:C} per unit");
    }

    Console.WriteLine();
    if (problems > 0)
    {
        Console.WriteLine($"{problems} problem(s) found. See UC0 in plan.md for how to correct the seed.");
        return 1;
    }

    Console.WriteLine("Sandbox is correctly seeded. Configure the integration with:");
    Console.WriteLine($"  Maxio:ProductFamilyHandle    = {family.Handle}");
    Console.WriteLine($"  Maxio:ProductFamilyId        = {family.Id}");
    Console.WriteLine($"  Maxio:DefaultProductHandle   = {definition.DefaultPlanHandle}");
    Console.WriteLine($"  Maxio:DefaultProductId       = {plans.First(p => Matches(p.Handle, definition.DefaultPlanHandle)).Id}");
    Console.WriteLine($"  Maxio:AlternateProductHandle = {definition.AlternatePlanHandle}");
    Console.WriteLine($"  Maxio:AlternateProductId     = {plans.First(p => Matches(p.Handle, definition.AlternatePlanHandle)).Id}");
    Console.WriteLine($"  Maxio:MeteredComponentHandle = {definition.MeteredComponentHandle}");
    Console.WriteLine($"  Maxio:MeteredComponentId     = {component!.Id}");

    return 0;
}

static async Task<int> EnsurePlanAsync(IBillingProvisioningClient client,
    BillingProductFamily family,
    string handle,
    string name,
    string description,
    decimal price,
    bool verifyOnly,
    CancellationToken cancellationToken)
{
    var plans = await client.ListPlansForFamilyAsync(family, false, cancellationToken);
    var existing = plans.FirstOrDefault(p => Matches(p.Handle, handle));

    if (existing is null)
    {
        if (verifyOnly)
        {
            Console.WriteLine($"MISSING  plan '{handle}'");
            return 1;
        }

        var created = await client.CreatePlanAsync(family, handle, name, description, price,
            SeedDefinition.BillingInterval, SeedDefinition.BillingIntervalUnit,
            SeedDefinition.RequiresPaymentMethod, cancellationToken);

        Console.WriteLine($"CREATED  plan '{created.Handle}' (id {created.Id}) at {created.BillingDescription}");
        return 0;
    }

    Console.WriteLine($"EXISTS   plan '{existing.Handle}' (id {existing.Id}) at {existing.BillingDescription}");

    // An existing entity is never mutated in place — mismatches are reported for the operator to decide.
    var problems = 0;
    if (existing.Price != price)
    {
        Console.WriteLine($"  WARN   price is {existing.Price:C}, expected {price:C}");
        problems++;
    }

    if (existing.RequiresPaymentMethod != SeedDefinition.RequiresPaymentMethod)
    {
        Console.WriteLine("  WARN   this plan requires a payment method, so subscribing will demand card capture");
        problems++;
    }

    if (!Matches(existing.IntervalUnit, SeedDefinition.BillingIntervalUnit) ||
        existing.Interval != SeedDefinition.BillingInterval)
    {
        Console.WriteLine($"  WARN   billing cadence is every {existing.Interval} {existing.IntervalUnit}(s), " +
                          $"expected every {SeedDefinition.BillingInterval} {SeedDefinition.BillingIntervalUnit}");
        problems++;
    }

    return problems;
}

static async Task<int> EnsureComponentAsync(IBillingProvisioningClient client,
    BillingProductFamily family,
    SeedDefinition definition,
    bool verifyOnly,
    CancellationToken cancellationToken)
{
    var components = await client.ListComponentsForFamilyAsync(family, false, cancellationToken);
    var existing = components.FirstOrDefault(c => Matches(c.Handle, definition.MeteredComponentHandle));

    if (existing is not null && existing.IsMetered)
    {
        Console.WriteLine($"EXISTS   component '{existing.Handle}' (id {existing.Id}) at {existing.UnitPrice:C} per unit");

        if (existing.UnitPrice != SeedDefinition.ComponentUnitPrice)
        {
            Console.WriteLine($"  WARN   unit price is {existing.UnitPrice:C}, expected {SeedDefinition.ComponentUnitPrice:C}");
            return 1;
        }

        return 0;
    }

    if (existing is not null)
    {
        // A component's kind cannot be changed in place, so the wrong one is archived and replaced.
        Console.WriteLine($"WRONG    component '{existing.Handle}' (id {existing.Id}) is {existing.Kind}, not metered");

        if (verifyOnly)
        {
            return 1;
        }

        await client.ArchiveComponentAsync(family, existing.Id, cancellationToken);
        Console.WriteLine($"ARCHIVED component id {existing.Id} so a metered one can take its place");
    }
    else if (verifyOnly)
    {
        Console.WriteLine($"MISSING  component '{definition.MeteredComponentHandle}'");
        return 1;
    }

    var created = await client.CreateMeteredComponentAsync(family, definition.MeteredComponentHandle,
        SeedDefinition.ComponentName, SeedDefinition.ComponentUnitName, SeedDefinition.ComponentUnitPrice,
        cancellationToken);

    Console.WriteLine($"CREATED  component '{created.Handle}' (id {created.Id}) at {created.UnitPrice:C} per unit");
    return 0;
}

static bool Matches(string? left, string right) =>
    string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

static string Fallback(string? configured, string standard) =>
    string.IsNullOrWhiteSpace(configured) ? standard : configured.Trim();
