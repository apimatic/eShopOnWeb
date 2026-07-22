using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

/// <summary>
/// The UC0 provisioning surface used by the one-shot seeding tool.
/// </summary>
public class ProvisioningOperations
{
    private readonly StubHttpMessageHandler _handler = new();

    private static BillingProductFamily Family() => new(3026731, "eshop-subscribe", "eShopSubscribe");

    [Fact]
    public async Task FindsTheProductFamilyByMatchingItsHandleInTheList()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductFamilyList(ProviderPayloads.ProductFamily));

        var family = await BillingClientFixture.Create(_handler)
            .FindProductFamilyByHandleAsync("eshop-subscribe");

        Assert.NotNull(family);
        Assert.Equal(3026731, family.Id);
        Assert.Equal("eShopSubscribe", family.Name);
    }

    [Fact]
    public async Task ReturnsNullWhenNoFamilyCarriesTheHandle()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductFamilyList(ProviderPayloads.ProductFamily));

        var family = await BillingClientFixture.Create(_handler)
            .FindProductFamilyByHandleAsync("not-seeded");

        Assert.Null(family);
    }

    [Fact]
    public async Task CreatesAPlanSendingThePriceInCents()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductResponse(ProviderPayloads.ProPlanProduct));

        var plan = await BillingClientFixture.Create(_handler).CreatePlanAsync(Family(), "eshop-pro", "Pro Plan",
            "The eShopOnWeb Pro subscription.", 299.00m, 1, "month", false);

        // $299.00 must go out as 29900 cents, not 299.
        Assert.Contains("\"price_in_cents\":29900", _handler.LastRequest.Body);
        Assert.Contains("\"interval_unit\":\"month\"", _handler.LastRequest.Body);
        Assert.Contains("\"require_credit_card\":false", _handler.LastRequest.Body);
        Assert.Equal(299.00m, plan.Price);
    }

    [Fact]
    public async Task RoundsAFractionalPriceToWholeCents()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductResponse(ProviderPayloads.BasicPlanProduct));

        await BillingClientFixture.Create(_handler).CreatePlanAsync(Family(), "basic-plan", "Basic Plan",
            "desc", 29.995m, 1, "month", false);

        Assert.Contains("\"price_in_cents\":3000", _handler.LastRequest.Body);
    }

    [Fact]
    public async Task RefusesABillingIntervalTheProviderDoesNotSupport()
    {
        var exception = await Assert.ThrowsAsync<BillingConfigurationException>(
            () => BillingClientFixture.Create(_handler).CreatePlanAsync(Family(), "h", "n", "d", 1m, 1,
                "fortnight", false));

        Assert.Contains("fortnight", exception.Message);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task CreatesTheMeteredComponentSendingTheUnitPriceInCurrencyUnits()
    {
        _handler.RespondWithJson(ProviderPayloads.ComponentResponse(ProviderPayloads.MeteredComponent));

        var component = await BillingClientFixture.Create(_handler)
            .CreateMeteredComponentAsync(Family(), "api-call", "API Calls", "API call", 0.01m);

        // unit_price is decimal currency units here, unlike the plan's price_in_cents.
        Assert.Contains("\"unit_price\":\"0.01\"", _handler.LastRequest.Body);
        Assert.Contains("\"pricing_scheme\":\"per_unit\"", _handler.LastRequest.Body);
        Assert.True(component.IsMetered);
        Assert.Equal(0.01m, component.UnitPrice);
    }

    [Fact]
    public async Task ListsThePlansDefinedOnAFamily()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductList(
            ProviderPayloads.ProPlanProduct, ProviderPayloads.BasicPlanProduct));

        var plans = await BillingClientFixture.Create(_handler).ListPlansForFamilyAsync(Family(), false);

        Assert.Equal(2, plans.Count);
        Assert.Contains("include_archived=false", _handler.LastRequest.Uri.Query);
    }

    [Fact]
    public async Task ListsTheComponentsDefinedOnAFamily()
    {
        _handler.RespondWithJson($"[{ProviderPayloads.ComponentResponse(ProviderPayloads.MeteredComponent)}]");

        var components = await BillingClientFixture.Create(_handler)
            .ListComponentsForFamilyAsync(Family(), false);

        Assert.Single(components);
        Assert.True(components.Single().IsMetered);
        Assert.Contains("/product_families/3026731/components.json", _handler.LastRequest.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ArchivesAMisCreatedComponentRatherThanMutatingIt()
    {
        // This operation answers with the component directly, not the usual envelope.
        _handler.RespondWithJson(ProviderPayloads.QuantityBasedComponent);

        var archived = await BillingClientFixture.Create(_handler)
            .ArchiveComponentAsync(Family(), 3062799);

        Assert.Equal(3062799, archived.Id);
        Assert.Equal(HttpMethod.Delete, _handler.LastRequest.Method);
    }

    [Fact]
    public async Task ArchivesAMisCreatedPlan()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductResponse(ProviderPayloads.BasicPlanProduct));

        var archived = await BillingClientFixture.Create(_handler).ArchivePlanAsync(7131000);

        Assert.Equal("basic-plan", archived.Handle);
    }

    [Fact]
    public async Task CreatesTheProductFamilyWithItsStableHandle()
    {
        _handler.RespondWithJson(ProviderPayloads.ProductFamilyResponse(ProviderPayloads.ProductFamily));

        var family = await BillingClientFixture.Create(_handler)
            .CreateProductFamilyAsync("eshop-subscribe", "eShopSubscribe", "Recurring plans.");

        Assert.Equal("eshop-subscribe", family.Handle);
        Assert.Contains("\"handle\":\"eshop-subscribe\"", _handler.LastRequest.Body);
    }

    [Fact]
    public async Task SurfacesARejectedSeedWithTheProvidersValidationMessages()
    {
        _handler.RespondWithError(HttpStatusCode.UnprocessableEntity,
            """{"errors": ["Handle: has already been taken."]}""");

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => BillingClientFixture.Create(_handler)
                .CreateProductFamilyAsync("eshop-subscribe", "eShopSubscribe", null));

        Assert.Equal(422, exception.StatusCode);
        Assert.Contains("already been taken", exception.ProviderMessage);
    }
}
