using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;
using Microsoft.eShopWeb.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// UC0 operator tooling — NOT wired into the Web/PublicApi hosts (§4.1). Verifies (read-only)
// that the product family, both plans, and the metered component already seeded on the sandbox
// match this integration's configuration. Per the plan, the sandbox is expected to already be
// seeded, so this tool only ever reads the provider — it never creates or archives entities.
var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.Configure<MaxioSettings>(configuration.GetSection("Maxio"));
services.AddHttpClient<IBillingClient, MaxioBillingClient>();
var provider = services.BuildServiceProvider();

var settings = provider.GetRequiredService<IOptions<MaxioSettings>>().Value;
var billingClient = provider.GetRequiredService<IBillingClient>();

Console.WriteLine($"UC0 seed verification — sandbox subdomain '{settings.Subdomain}', family id {settings.ProductFamilyId}");
Console.WriteLine(new string('-', 72));

var problems = new List<string>();

try
{
    var plans = await billingClient.ListPlansAsync();

    ReportPlan(plans, settings.DefaultProductHandle, "default (hero)", problems);
    ReportPlan(plans, settings.AlternateProductHandle, "alternate", problems);
}
catch (Exception ex)
{
    problems.Add($"Could not list plans for product family {settings.ProductFamilyId}: {ex.Message}");
}

try
{
    var component = await billingClient.GetMeteredComponentAsync();
    Console.WriteLine($"Metered component '{component.Handle}': kind is metered = {component.IsMetered}");
    if (!component.IsMetered)
    {
        problems.Add($"Component '{component.Handle}' does not resolve to a metered-kind component.");
    }
}
catch (Exception ex)
{
    problems.Add($"Could not resolve metered component '{settings.MeteredComponentHandle}': {ex.Message}");
}

Console.WriteLine(new string('-', 72));

if (problems.Count == 0)
{
    Console.WriteLine("UC0 seed verification PASSED — the sandbox matches this integration's configuration.");
    return 0;
}

Console.WriteLine("UC0 seed verification FAILED:");
foreach (var problem in problems)
{
    Console.WriteLine($"  - {problem}");
}

return 1;

static void ReportPlan(IReadOnlyList<BillingPlan> plans, string expectedHandle, string role, List<string> problems)
{
    var plan = plans.FirstOrDefault(p => string.Equals(p.Handle, expectedHandle, StringComparison.OrdinalIgnoreCase));
    if (plan is null)
    {
        problems.Add($"Plan handle '{expectedHandle}' ({role}) was not found in the product family.");
        Console.WriteLine($"Plan '{expectedHandle}' ({role}): MISSING");
        return;
    }

    Console.WriteLine($"Plan '{plan.Handle}' ({role}): '{plan.Name}', $ {plan.Price:N2} / {plan.BillingIntervalUnit}, requires payment method = {plan.RequiresPaymentMethod}");
    if (plan.RequiresPaymentMethod)
    {
        problems.Add($"Plan '{plan.Handle}' requires a payment method — the demo enrollment path does not collect card details.");
    }
}
