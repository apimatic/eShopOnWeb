using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Servers;

// UC0 operator tooling (plan.md §"UC0 - Seed the Billing Provider Sandbox"): verifies the product
// family, both recurring plans, and the metered usage component already exist on the sandbox with
// the expected handles/shape. This is a one-shot console check, never wired into the Web/PublicApi
// hosts. Credentials come from environment variables only - never hardcoded, never committed.

const string ExpectedProductFamilyHandle = "eshop-subscribe";

const string DefaultProductHandle = "eshop-pro";
const long DefaultProductExpectedPriceInCents = 29900;

const string AlternateProductHandle = "basic-plan";
const long AlternateProductExpectedPriceInCents = 2900;

const string MeteredComponentHandle = "api-call";
const long MeteredComponentExpectedPricePerUnitInCents = 1;

var apiKey = Environment.GetEnvironmentVariable("MAXIO_API_KEY");
var subdomain = Environment.GetEnvironmentVariable("MAXIO_SITE_SUBDOMAIN");
var region = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT") ?? "US";

if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(subdomain))
{
    Console.Error.WriteLine("MAXIO_API_KEY and MAXIO_SITE_SUBDOMAIN environment variables are required.");
    return 1;
}

var options = new MaxioAdvancedBillingClientOptions
{
    BasicAuth = new BasicAuthCredentials { Username = apiKey, Password = "x" },
    Environment = string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase) ? ServerEnvironment.Eu : ServerEnvironment.Us,
};

if (options.Environment == ServerEnvironment.Eu)
{
    options.Server.Production.Eu.Site = subdomain;
}
else
{
    options.Server.Production.Us.Site = subdomain;
}

using var httpClient = new HttpClient();
var client = new MaxioAdvancedBillingClient(httpClient, options);

var failures = 0;

Console.WriteLine($"Verifying Maxio sandbox seed on subdomain '{subdomain}' ({region})...");
Console.WriteLine();

// --- Both products, resolved by handle (also confirms the family via each product's embedded ProductFamily) ---
await CheckProductByHandleAsync(DefaultProductHandle, DefaultProductExpectedPriceInCents);
await CheckProductByHandleAsync(AlternateProductHandle, AlternateProductExpectedPriceInCents);

// --- Metered usage component ---
try
{
    var response = await client.Components.FindComponent(MeteredComponentHandle);
    var component = response.Component;
    Check($"Component '{MeteredComponentHandle}' resolves", component is not null);
    Check($"Component '{MeteredComponentHandle}' kind == Metered", component?.Kind == ComponentKind.MeteredComponent);

    // Live-verified: this sandbox's FindComponent response leaves price_per_unit_in_cents null and
    // only populates the display-string unit_price - the reverse of what the SDK map's doc comments
    // suggested. Parse the string field instead rather than trusting the integer-cents field blindly.
    var priceMatches = component?.UnitPrice is { } unitPrice
        && decimal.TryParse(unitPrice, out var unitPriceDecimal)
        && Math.Round(unitPriceDecimal * 100, 0) == MeteredComponentExpectedPricePerUnitInCents;
    Console.WriteLine($"       actual price_per_unit_in_cents = {component?.PricePerUnitInCents.ToString() ?? "(null)"}, unit_price = {component?.UnitPrice ?? "(null)"}");
    Check($"Component '{MeteredComponentHandle}' price == {MeteredComponentExpectedPricePerUnitInCents} cents/unit", priceMatches);
}
catch (SdkException<RawError> ex)
{
    Fail($"Could not read component '{MeteredComponentHandle}': {Describe(ex.Error)}");
}
catch (Exception ex)
{
    Fail($"Could not read component '{MeteredComponentHandle}': {ex.Message}");
}

async Task CheckProductByHandleAsync(string handle, long expectedPriceInCents)
{
    try
    {
        var response = await client.Products.ReadProductByHandle(handle);
        var product = response.Product;
        Check($"Product '{handle}' resolves", product is not null);
        Check($"Product '{handle}' family handle == '{ExpectedProductFamilyHandle}'", product?.ProductFamily?.Handle == ExpectedProductFamilyHandle);
        Console.WriteLine($"       actual price_in_cents = {product?.PriceInCents.ToString() ?? "(null)"}, require_credit_card = {product?.RequireCreditCard.ToString() ?? "(null)"}");
        Check($"Product '{handle}' price == {expectedPriceInCents} cents", product?.PriceInCents == expectedPriceInCents);
        Check($"Product '{handle}' requires payment method == false", product?.RequireCreditCard == false);
    }
    catch (SdkException<RawError> ex)
    {
        Fail($"Could not read product '{handle}': {Describe(ex.Error)}");
    }
    catch (Exception ex)
    {
        Fail($"Could not read product '{handle}': {ex.Message}");
    }
}

Console.WriteLine();
if (failures == 0)
{
    Console.WriteLine("UC0 seed verification PASSED - the sandbox matches the expected shape.");
    return 0;
}

Console.WriteLine($"UC0 seed verification FAILED - {failures} check(s) did not pass. See plan.md UC0 failure scenarios.");
return 1;

void Check(string description, bool passed)
{
    Console.WriteLine($"  [{(passed ? "PASS" : "FAIL")}] {description}");
    if (!passed)
    {
        failures++;
    }
}

void Fail(string message)
{
    Console.WriteLine($"  [FAIL] {message}");
    failures++;
}

string Describe(RawError error)
{
    try
    {
        return $"{(int)error.StatusCode} {error.StatusCode}: {error.ReadAsString()}";
    }
    catch
    {
        return error.StatusCode.ToString();
    }
}
