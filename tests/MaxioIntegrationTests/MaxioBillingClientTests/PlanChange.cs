using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Xunit;

namespace Microsoft.eShopWeb.MaxioIntegrationTests.MaxioBillingClientTests;

public class PlanChange
{
    [Fact]
    public async Task PreviewConvertsEveryProrationAmountFromCents()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.MigrationPreview);
        var client = BillingClientFixture.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(15236915, "eshop-pro");

        Assert.Equal(250.00m, preview.ProratedAdjustment);
        Assert.Equal(270.00m, preview.Charge);
        Assert.Equal(260.00m, preview.PaymentDue);
        Assert.Equal(10.00m, preview.CreditApplied);
        Assert.Equal(15236915, preview.SubscriptionId);
        Assert.Equal("eshop-pro", preview.TargetPlanHandle);
    }

    [Fact]
    public async Task PreviewKeepsTheSignOfACreditOnADowngrade()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.MigrationPreviewWithCredit);
        var client = BillingClientFixture.Create(handler);

        var preview = await client.PreviewPlanChangeAsync(15236915, "basic-plan");

        Assert.Equal(-135.00m, preview.ProratedAdjustment);
        Assert.Equal(135.00m, preview.CreditApplied);
    }

    [Fact]
    public async Task PreviewSendsTheTargetPlanHandleAndCommitsNothing()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.MigrationPreview);
        var client = BillingClientFixture.Create(handler);

        await client.PreviewPlanChangeAsync(15236915, "basic-plan");

        var sent = Assert.Single(handler.Requests);
        Assert.Contains("preview", sent.Uri.AbsolutePath);
        Assert.Contains("\"product_handle\":\"basic-plan\"", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task ChangePlanNowMigratesTheSubscriptionSoProrationApplies()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription);
        var client = BillingClientFixture.Create(handler);

        var subscription = await client.ChangePlanNowAsync(15236915, "eshop-pro");

        Assert.Equal(15236915, subscription.Id);

        var sent = handler.LastRequest;
        Assert.Contains("migrations", sent.Uri.AbsolutePath);
        Assert.DoesNotContain("preview", sent.Uri.AbsolutePath);
        Assert.Contains("\"product_handle\":\"eshop-pro\"", sent.Body.Replace(" ", string.Empty));
    }

    [Fact]
    public async Task ChangePlanAtRenewalDefersTheChangeSoNoProrationApplies()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ActiveSubscription);
        var client = BillingClientFixture.Create(handler);

        await client.ChangePlanAtRenewalAsync(15236915, "basic-plan");

        var body = handler.LastRequest.Body.Replace(" ", string.Empty);
        Assert.Contains("\"product_handle\":\"basic-plan\"", body);
        Assert.Contains("\"product_change_delayed\":true", body);
    }

    [Fact]
    public async Task PreviewSurfacesAProviderRejectionAsATypedException()
    {
        var handler = StubHttpMessageHandler.Always(ProviderPayloads.ValidationErrors,
            HttpStatusCode.UnprocessableEntity);
        var client = BillingClientFixture.Create(handler);

        var exception = await Assert.ThrowsAsync<BillingProviderException>(
            () => client.PreviewPlanChangeAsync(15236915, "no-such-plan"));

        Assert.Equal(422, exception.StatusCode);
    }
}
