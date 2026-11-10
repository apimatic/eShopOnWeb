using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.eShopWeb.PublicApi.Mindbody;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.StudioEndpoints;

[TestClass]
public class StudioEndpointsTest
{
    private static HttpClient ClientWith(string token)
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [TestMethod]
    [DataRow("api/studio/classes")]
    [DataRow("api/studio/slots?sessionTypeId=1")]
    [DataRow("api/studio/admin/trainers")]
    public async Task RequiresAToken(string url)
    {
        var response = await ProgramTest.NewClient.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task TrainersAreForAdministratorsOnly()
    {
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken()).GetAsync("api/studio/admin/trainers");
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task AdminBookingIsForAdministratorsOnly()
    {
        var response = await ClientWith(ApiTokenHelper.GetNormalUserToken())
            .PostAsync("api/studio/admin/class-bookings", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public void BaseUrlOverrideIsUsedVerbatim()
    {
        var overridden = MindbodyClient.ResolveBaseAddress(new MindbodyOptions { BaseUrl = "http://localhost:9999/mock/v6" });
        Assert.AreEqual("http://localhost:9999/mock/v6/", overridden.ToString());
        Assert.AreEqual("http://localhost:9999/mock/v6/class/classes", new Uri(overridden, "class/classes").ToString());

        var defaulted = MindbodyClient.ResolveBaseAddress(new MindbodyOptions());
        Assert.AreEqual("https://api.mindbodyonline.com/public/v6/", defaulted.ToString());
    }

    [TestMethod]
    public async Task KeyedLockSerialisesSameKeyOnly()
    {
        var locks = new KeyedLock();
        using var first = await locks.AcquireAsync("a");
        using var other = await locks.AcquireAsync("b");

        var waiting = locks.AcquireAsync("a");
        await Task.Delay(50);
        Assert.IsFalse(waiting.IsCompleted);
        first.Dispose();
        using var second = await waiting;
    }
}
