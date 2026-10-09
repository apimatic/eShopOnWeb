using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

public class FormattingTests
{
    [Fact]
    public void AuthorizationRequests_GetDistinctStates_AndOnlyRecogniseTheirOwn()
    {
        var first = AuthorizationRequest.Create("https://connect.squareupsandbox.com", "app", "http://localhost:1/cb");
        var second = AuthorizationRequest.Create("https://connect.squareupsandbox.com", "app", "http://localhost:1/cb");

        Assert.NotEqual(first.State, second.State);
        Assert.True(first.IsOwnState(first.State));
        Assert.False(first.IsOwnState(second.State));
        Assert.False(first.IsOwnState(null));
        Assert.False(first.IsOwnState(""));
    }

    [Fact]
    public void AuthorizationRequest_EscapesTheRedirectAddress()
    {
        var request = AuthorizationRequest.Create("https://connect.squareup.com/", "app id", "http://localhost:8080/callback");

        Assert.StartsWith("https://connect.squareup.com/oauth2/authorize?client_id=app%20id&response_type=code&scope=MERCHANT_PROFILE_READ&state=",
            request.SignInPage.AbsoluteUri);
        Assert.EndsWith("&redirect_uri=http%3A%2F%2Flocalhost%3A8080%2Fcallback", request.SignInPage.AbsoluteUri);
    }

    [Fact]
    public void ConnectedPage_EncodesTheBusinessName()
    {
        var page = SignInPages.Connected("<script>alert(1)</script>");

        Assert.DoesNotContain("<script>", page.Html);
        Assert.Contains("&lt;script&gt;", page.Html);
    }

    [Fact]
    public void Report_ListsEveryLocationWithNameStatusAndAddress()
    {
        var output = new StringWriter();

        AccountReport.Write(output, "sandbox", new MerchantSummary("M1", "Shop"),
        [
            new LocationSummary("L1", "Front", "ACTIVE", "1 A St, Town"),
            new LocationSummary(null, null, "INACTIVE", null),
        ]);

        var text = output.ToString();
        Assert.Contains("Connected to Square (sandbox).", text);
        Assert.Contains("Business: Shop", text);
        Assert.Contains("Merchant ID: M1", text);
        Assert.Contains("2 locations:", text);
        Assert.Contains("  Front", text);
        Assert.Contains("Address: 1 A St, Town", text);
        Assert.Contains("(unnamed location)", text);
        Assert.Contains("Status:  INACTIVE", text);
    }

    [Fact]
    public void MerchantWithoutBusinessName_IsNamedByItsId()
    {
        Assert.Equal("M1", new MerchantSummary("M1", null).DisplayName);
        Assert.Equal("Shop", new MerchantSummary("M1", "Shop").DisplayName);
    }
}
