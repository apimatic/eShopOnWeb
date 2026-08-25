using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace PublicApiIntegrationTests.OrderEndpoints;

[TestClass]
public class CancelOrderEndpointTest
{
    [TestMethod]
    public async Task ReturnsForbiddenForNonAdminUser()
    {
        var client = ProgramTest.NewClient;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ApiTokenHelper.GetNormalUserToken());

        var response = await client.PostAsync("api/orders/1/cancel", null);

        Assert.AreEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
