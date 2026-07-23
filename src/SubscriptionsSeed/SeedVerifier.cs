using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Configuration;

namespace Microsoft.eShopWeb.SubscriptionsSeed;

/// <summary>
/// Reads the billing provider back and reports whether it holds the entities the integration is
/// configured to use (UC0's verification step). It only ever reads — correcting a bad seed is done
/// deliberately by an operator, because a component of the wrong kind has to be archived and
/// recreated rather than mutated in place.
/// </summary>
public class SeedVerifier
{
    private const int Ok = 0;
    private const int Failed = 1;

    public async Task<int> VerifyAsync(IBillingClient billingClient, MaxioSettings settings)
    {
        Console.WriteLine($"Verifying the billing seed on '{settings.Subdomain}' " +
                          $"({settings.ResolveBaseUrl()})");
        Console.WriteLine();

        var problems = new List<string>();

        try
        {
            await VerifyPlansAsync(billingClient, settings, problems);
            await VerifyComponentAsync(billingClient, settings, problems);
        }
        catch (BillingProviderAuthenticationException)
        {
            Console.Error.WriteLine("FAIL  The configured API key was rejected. Set Maxio:ApiKey in " +
                                    "user-secrets (or the Maxio__ApiKey environment variable).");
            return Failed;
        }
        catch (BillingConfigurationException exception)
        {
            Console.Error.WriteLine($"FAIL  {exception.Message}");
            return Failed;
        }
        catch (BillingProviderException exception)
        {
            Console.Error.WriteLine($"FAIL  The billing provider could not be reached: {exception.Message}");
            return Failed;
        }

        Console.WriteLine();

        if (problems.Count == 0)
        {
            Console.WriteLine("PASS  The seed matches the configuration. UC1-UC4 can run against it.");
            return Ok;
        }

        Console.Error.WriteLine($"FAIL  {problems.Count} problem(s) found:");
        foreach (var problem in problems)
        {
            Console.Error.WriteLine($"      - {problem}");
        }

        return Failed;
    }

    private static async Task VerifyPlansAsync(IBillingClient billingClient,
        MaxioSettings settings,
        List<string> problems)
    {
        var plans = await billingClient.ListPlansAsync();

        Console.WriteLine($"Product family '{settings.ProductFamilyHandle}' lists {plans.Count} plan(s):");
        foreach (var plan in plans)
        {
            Console.WriteLine($"  - {plan.Handle,-16} id {plan.Id,-10} {plan.Price,10:N2} " +
                              $"every {plan.Interval} {plan.IntervalUnit}" +
                              (plan.RequiresPaymentMethod ? "   [requires a payment method]" : string.Empty));
        }

        foreach (var handle in new[] { settings.DefaultProductHandle, settings.AlternateProductHandle })
        {
            if (string.IsNullOrWhiteSpace(handle))
            {
                continue;
            }

            var plan = await billingClient.FindPlanByHandleAsync(handle);

            if (plan is null)
            {
                problems.Add($"Plan '{handle}' does not resolve in family " +
                             $"'{settings.ProductFamilyHandle}'. Create it, or correct the configured handle.");
            }
            else if (plan.RequiresPaymentMethod)
            {
                problems.Add($"Plan '{handle}' requires a payment method, so subscribing will demand " +
                             "card capture. Turn that toggle off for the demo path.");
            }
        }
    }

    private static async Task VerifyComponentAsync(IBillingClient billingClient,
        MaxioSettings settings,
        List<string> problems)
    {
        var handle = settings.MeteredComponentHandle;

        if (string.IsNullOrWhiteSpace(handle))
        {
            problems.Add("No metered component handle is configured, so pay-as-you-go usage cannot be billed.");
            return;
        }

        var component = await billingClient.FindComponentByHandleAsync(handle);

        if (component is null)
        {
            problems.Add($"Metered component '{handle}' does not exist on family " +
                         $"'{settings.ProductFamilyHandle}'. Create it as a metered component.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"Component '{component.Handle}' id {component.Id}: kind '{component.Kind}', " +
                          $"scheme '{component.PricingScheme}', unit price {component.UnitPrice:N2} per unit");

        if (!component.IsMetered)
        {
            problems.Add($"Component '{handle}' is of kind '{component.Kind}', not metered. A component's " +
                         "kind cannot be changed in place — archive it and recreate it as metered.");
        }
    }
}
