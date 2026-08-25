using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CancelOrderEndpointTest
{
    [TestMethod]
    public async Task ReturnsForbiddenForNormalUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/999999/cancel", content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TestMethod]
    public async Task ReturnsNotFoundForUnknownOrderAsAdmin()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetAdminUserToken());

        var response = await client.PostAsync("api/orders/999999/cancel", content: null);

        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
