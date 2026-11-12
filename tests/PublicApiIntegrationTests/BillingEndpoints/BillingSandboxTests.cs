using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using MaxioAdvancedBilling.Requests.Components;
using MaxioAdvancedBilling.Requests.Invoices;
using MaxioAdvancedBilling.Requests.Products;
using MaxioAdvancedBilling.Requests.SubscriptionComponents;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.BillingEndpoints;

/// <summary>
/// End-to-end tests of the billing endpoints against the real Maxio sandbox (credentials come from the
/// "Maxio:" configuration section, i.e. user-secrets). Nothing is stubbed.
/// </summary>
[TestClass]
public class BillingSandboxTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static WebApplicationFactory<Program> _factory = null!;
    private static string _brand = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        _factory = new WebApplicationFactory<Program>();
        using var client = Client(null);
        var brands = await client.GetFromJsonAsync("api/catalog-brands");
        _brand = brands.GetProperty("catalogBrands")[0].GetProperty("name").GetString()!;
    }

    [ClassCleanup]
    public static void Cleanup() => _factory.Dispose();

    private static HttpClient Client(string? token)
    {
        var client = _factory.CreateClient();
        if (token != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    private static HttpClient Admin() => Client(ApiTokenHelper.GetAdminUserToken());
    private static HttpClient Shopper() => Client(ApiTokenHelper.GetNormalUserToken());

    private static StringContent Body(object o) => new(JsonSerializer.Serialize(o, Json), Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadJson(HttpResponseMessage r, HttpStatusCode expected)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.AreEqual(expected, r.StatusCode, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> CreatePlan(decimal price = 12.50m)
    {
        var name = "eShop test plan " + Guid.NewGuid().ToString("N").Substring(0, 10);
        using var admin = Admin();
        return await ReadJson(await admin.PostAsync("api/admin/plans", Body(new { name, monthlyPrice = price, brand = _brand })), HttpStatusCode.Created);
    }

    [TestMethod]
    public async Task Plans_AreCreatedByStaffAndListedForShoppers()
    {
        var plan = await CreatePlan(12.50m);
        Assert.AreEqual(1250, plan.GetProperty("priceInCents").GetInt64());
        Assert.AreEqual(_brand, plan.GetProperty("brand").GetString());

        using var shopper = Shopper();
        var list = await ReadJson(await shopper.GetAsync("api/plans"), HttpStatusCode.OK);
        Assert.IsTrue(list.EnumerateArray().Any(p => p.GetProperty("handle").GetString() == plan.GetProperty("handle").GetString()));
    }

    [TestMethod]
    public async Task Plans_RejectUnknownBrandAndNonStaff()
    {
        using var admin = Admin();
        var bad = await admin.PostAsync("api/admin/plans", Body(new { name = "x" + Guid.NewGuid(), monthlyPrice = 5, brand = "No such brand " + Guid.NewGuid() }));
        Assert.IsTrue((int)bad.StatusCode is >= 400 and < 500, await bad.Content.ReadAsStringAsync());

        using var shopper = Shopper();
        var forbidden = await shopper.PostAsync("api/admin/plans", Body(new { name = "y", monthlyPrice = 5, brand = _brand }));
        Assert.AreEqual(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anon = Client(null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anon.GetAsync("api/plans")).StatusCode);
    }

    [TestMethod]
    public async Task Subscription_FullLifecycle()
    {
        var planA = await CreatePlan(10m);
        var planB = await CreatePlan(20m);
        var handleA = planA.GetProperty("handle").GetString();
        var handleB = planB.GetProperty("handle").GetString();

        using var shopper = Shopper();
        var created = await ReadJson(await shopper.PostAsync("api/subscriptions", Body(new { planHandle = handleA })), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();
        Assert.AreEqual(handleA, created.GetProperty("plan").GetProperty("handle").GetString());
        Assert.IsTrue(created.TryGetProperty("paid", out _));

        // several plans at once
        var second = await ReadJson(await shopper.PostAsync("api/subscriptions", Body(new { planHandle = handleB })), HttpStatusCode.Created);
        Assert.AreNotEqual(id, second.GetProperty("id").GetInt32());

        var page = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}"), HttpStatusCode.OK);
        Assert.AreEqual(id, page.GetProperty("id").GetInt32());

        // not offered / not mine
        Assert.AreEqual(HttpStatusCode.NotFound, (await shopper.PostAsync("api/subscriptions", Body(new { planHandle = "nope-" + Guid.NewGuid() }))).StatusCode);
        using var other = Client(ApiTokenHelper.GetAdminUserToken());
        Assert.AreEqual(HttpStatusCode.NotFound, (await other.GetAsync($"api/subscriptions/{id}")).StatusCode);

        // wrong-state calls are 4xx
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/resume", null)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/restart", null)).StatusCode);

        // pause / resume
        var paused = await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/pause", null), HttpStatusCode.OK);
        Assert.IsTrue(new[] { "on_hold", "paused" }.Contains(paused.GetProperty("state").GetString()), paused.ToString());
        await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/resume", null), HttpStatusCode.OK);

        // switch while active
        var switched = await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/switch", Body(new { planHandle = handleB })), HttpStatusCode.OK);
        Assert.AreEqual(handleB, switched.GetProperty("plan").GetProperty("handle").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/switch", Body(new { planHandle = handleB }))).StatusCode);

        // cancel, switch while cancelled, restart
        var canceled = await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null), HttpStatusCode.OK);
        Assert.AreEqual("canceled", canceled.GetProperty("state").GetString());
        Assert.AreEqual(HttpStatusCode.Conflict, (await shopper.PostAsync($"api/subscriptions/{id}/cancel", null)).StatusCode);
        var back = await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/switch", Body(new { planHandle = handleA })), HttpStatusCode.OK);
        Assert.AreEqual(handleA, back.GetProperty("plan").GetProperty("handle").GetString());
        Assert.AreNotEqual("canceled", back.GetProperty("state").GetString());

        await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/cancel", null), HttpStatusCode.OK);
        var restarted = await ReadJson(await shopper.PostAsync($"api/subscriptions/{id}/restart", null), HttpStatusCode.OK);
        Assert.AreNotEqual("canceled", restarted.GetProperty("state").GetString());

        // clean up what this test created in the sandbox
        foreach (var sid in new[] { id, second.GetProperty("id").GetInt32() })
        {
            await shopper.PostAsync($"api/subscriptions/{sid}/cancel", null);
        }
    }

    [TestMethod]
    public async Task Paid_IsFalseWhileMoneyIsOwedAndTrueOnceSettled()
    {
        var plan = await CreatePlan(11m);
        using var shopper = Shopper();
        var created = await ReadJson(await shopper.PostAsync("api/subscriptions", Body(new { planHandle = plan.GetProperty("handle").GetString() })), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();
        Assert.IsFalse(created.GetProperty("paid").GetBoolean(), created.ToString());

        // The shopper pays outside this app; record that in Maxio directly.
        var client = _factory.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        await client.Invoices.RecordPaymentForSubscription(new RecordPaymentForSubscriptionRequest
        {
            SubscriptionId = id,
            Body = new RecordPaymentRequest
            {
                Payment = new CreatePayment { Amount = "11.00", Memo = "sandbox test", PaymentDetails = "sandbox", PaymentMethod = InvoicePaymentMethodType.Cash }
            }
        });

        var page = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}"), HttpStatusCode.OK);
        Assert.IsTrue(page.GetProperty("paid").GetBoolean(), page.ToString());
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task NextBillingDate_IsSetByStaff()
    {
        var plan = await CreatePlan(9m);
        using var shopper = Shopper();
        var created = await ReadJson(await shopper.PostAsync("api/subscriptions", Body(new { planHandle = plan.GetProperty("handle").GetString() })), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();

        var date = DateTime.UtcNow.Date.AddDays(45);
        using var admin = Admin();
        var updated = await ReadJson(await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Body(new { date = date.ToString("yyyy-MM-dd") })), HttpStatusCode.OK);
        Assert.AreEqual(date.Date, updated.GetProperty("nextBillingAt").GetDateTimeOffset().UtcDateTime.Date, updated.ToString());

        Assert.AreEqual(HttpStatusCode.Forbidden, (await shopper.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Body(new { date = date.ToString("yyyy-MM-dd") }))).StatusCode);
        var past = await admin.PutAsync($"api/admin/subscriptions/{id}/next-billing-date", Body(new { date = "2020-01-01" }));
        Assert.IsTrue((int)past.StatusCode is >= 400 and < 500);

        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }

    [TestMethod]
    public async Task CurrencyPrice_IsAddedByStaff()
    {
        var plan = await CreatePlan(15m);
        using var admin = Admin();
        var response = await admin.PostAsync($"api/admin/plans/{plan.GetProperty("id").GetInt32()}/currency-prices", Body(new { currency = "EUR", price = 14 }));
        var text = await response.Content.ReadAsStringAsync();
        // Multi-currency must be enabled on the sandbox site; a refusal is a clean 4xx with a reason.
        if (response.StatusCode == HttpStatusCode.Created)
        {
            Assert.AreEqual("EUR", JsonDocument.Parse(text).RootElement.GetProperty("currency").GetString());
        }
        else
        {
            Assert.IsTrue((int)response.StatusCode is >= 400 and < 500, text);
            Assert.IsTrue(text.Length > 0);
        }

        var missing = await admin.PostAsync("api/admin/plans/999999999/currency-prices", Body(new { currency = "EUR", price = 14 }));
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [TestMethod]
    public async Task Usage_IsReportedForTheChosenDay()
    {
        var plan = await CreatePlan(5m);
        var client = _factory.Services.GetRequiredService<MaxioAdvancedBillingClient>();
        var product = (await client.Products.ReadProduct(new ReadProductRequest { ProductId = plan.GetProperty("id").GetInt32() })).Product;
        var familyId = product.ProductFamily!.Id!.Value;

        // Another system records usage: set up a metered component and record some usage directly in Maxio.
        var component = (await client.Components.CreateMeteredComponent(new CreateMeteredComponentRequest
        {
            ProductFamilyId = familyId.ToString(),
            Body = new CreateMeteredComponent
            {
                MeteredComponent = new MeteredComponent
                {
                    Name = "API calls " + Guid.NewGuid().ToString("N").Substring(0, 8),
                    UnitName = "call",
                    PricingScheme = PricingScheme.PerUnit,
                    UnitPrice = "0.10"
                }
            }
        })).Component;

        using var shopper = Shopper();
        var created = await ReadJson(await shopper.PostAsync("api/subscriptions", Body(new { planHandle = plan.GetProperty("handle").GetString() })), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetInt32();

        await client.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = component.Id!.Value,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 7, Memo = "sandbox test" } }
        });
        await client.SubscriptionComponents.CreateUsage(new CreateUsageOperationRequest
        {
            SubscriptionIdOrReference = id,
            ComponentId = component.Id!.Value,
            Body = new CreateUsageRequest { Usage = new CreateUsage { Quantity = 3, Memo = "sandbox test 2" } }
        });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var usage = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}/usage?date={today}"), HttpStatusCode.OK);
        var comp = usage.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("componentId").GetInt32() == component.Id!.Value);
        Assert.AreEqual(10m, comp.GetProperty("totalQuantity").GetDecimal(), usage.ToString());

        var otherDay = await ReadJson(await shopper.GetAsync($"api/subscriptions/{id}/usage?date=2020-01-01"), HttpStatusCode.OK);
        var other = otherDay.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("componentId").GetInt32() == component.Id!.Value);
        Assert.AreEqual(0m, other.GetProperty("totalQuantity").GetDecimal());

        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage")).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await shopper.GetAsync($"api/subscriptions/{id}/usage?date=garbage")).StatusCode);
        await shopper.PostAsync($"api/subscriptions/{id}/cancel", null);
    }
}

internal static class HttpExtensions
{
    public static async Task<JsonElement> GetFromJsonAsync(this HttpClient client, string url)
    {
        var text = await client.GetStringAsync(url);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
