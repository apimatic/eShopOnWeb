using System.Net;
using Microsoft.eShopWeb.SquareCheck.Configuration;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;

namespace Microsoft.eShopWeb.SquareCheck.UnitTests;

/// <summary>
/// Runs only when SQUARECHECK_LIVE_TESTS=1, so the normal test run never touches the network.
/// Needs the SQUARE_* variables (sandbox only) and SQUARE_ACCESS_TOKEN, a sandbox seller's token used
/// here to check the Square calls — the tool itself never uses it.
/// </summary>
public sealed class LiveSandboxFactAttribute : FactAttribute
{
    public LiveSandboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SQUARECHECK_LIVE_TESTS") != "1")
        {
            Skip = "Live Square sandbox check; set SQUARECHECK_LIVE_TESTS=1 to run it.";
        }
    }
}

public class LiveSandboxTests
{
    private static SquareConnectionSettings SandboxSettings()
    {
        var configuration = SquareConfiguration.Load();
        Assert.True(SquareSettingsValidator.TryValidate(configuration, out var settings, out var problems), string.Join("; ", problems));
        Assert.Equal("sandbox", settings.EnvironmentName); // never point these checks at production
        return settings;
    }

    private static string SandboxAccessToken()
    {
        var token = Environment.GetEnvironmentVariable("SQUARE_ACCESS_TOKEN");
        Assert.False(string.IsNullOrWhiteSpace(token), "SQUARE_ACCESS_TOKEN is not set");
        return token!;
    }

    [LiveSandboxFact]
    public async Task SandboxSeller_MerchantAndLocationsAreReadable()
    {
        using var http = SquareClientFactory.CreateHttpClient();
        var factory = new SquareClientFactory(http, SandboxSettings());
        var reader = new SquareAccountReader(factory.CreateSignedIn(SandboxAccessToken()));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var merchant = await reader.GetMerchantAsync(deadline.Token);
        var locations = await reader.ListLocationsAsync(deadline.Token);

        Assert.False(string.IsNullOrWhiteSpace(merchant.Id));
        Assert.NotEmpty(locations);
        Assert.All(locations, location => Assert.False(string.IsNullOrWhiteSpace(location.Status)));

        var report = new StringWriter();
        AccountReport.Write(report, "sandbox", merchant, locations);
        Console.WriteLine(report);
    }

    [LiveSandboxFact]
    public async Task SandboxTokenEndpoint_RefusesAnUnknownCode_AsASquareFailure()
    {
        using var http = SquareClientFactory.CreateHttpClient();
        var settings = SandboxSettings();
        var exchange = new SquareTokenExchange(new SquareClientFactory(http, settings).CreateForTokenExchange(), settings);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var refused = await Assert.ThrowsAsync<SquareRequestException>(
            () => exchange.ExchangeAsync("not-a-real-authorization-code", deadline.Token));

        Assert.NotNull(refused.StatusCode);
        Assert.InRange((int)refused.StatusCode!.Value, 400, 499);
        Console.WriteLine(refused.Message);
    }

    [LiveSandboxFact]
    public async Task SandboxCalls_WithARejectedToken_AreASquareFailure()
    {
        using var http = SquareClientFactory.CreateHttpClient();
        var reader = new SquareAccountReader(new SquareClientFactory(http, SandboxSettings()).CreateSignedIn("not-a-valid-token"));

        var refused = await Assert.ThrowsAsync<SquareRequestException>(() => reader.GetMerchantAsync(CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Console.WriteLine(refused.Message);
    }
}
