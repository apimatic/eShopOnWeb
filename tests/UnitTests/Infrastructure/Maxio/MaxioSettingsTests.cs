using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Integration.Maxio;
using Xunit;

namespace Microsoft.eShopWeb.UnitTests.Infrastructure.Maxio;

public class MaxioSettingsTests
{
    [Fact]
    public void Derives_base_address_from_subdomain()
    {
        var settings = new MaxioSettings { Subdomain = "cp-exp-4" };

        Assert.Equal("https://cp-exp-4.chargify.com/", settings.GetApiBaseAddress().ToString());
    }

    [Fact]
    public void Explicit_base_url_is_used_verbatim_and_wins_over_subdomain()
    {
        var settings = new MaxioSettings
        {
            Subdomain = "ignored",
            BaseUrl = "https://sandbox.example.com/api",
        };

        var uri = settings.GetApiBaseAddress();

        Assert.Equal("https://sandbox.example.com/api/", uri.ToString());
    }

    [Fact]
    public void Missing_subdomain_and_base_url_throws()
    {
        var settings = new MaxioSettings();

        Assert.Throws<InvalidOperationException>(() => settings.GetApiBaseAddress());
    }

    [Fact]
    public void Missing_api_key_throws()
    {
        var settings = new MaxioSettings { Subdomain = "x" };

        Assert.Throws<InvalidOperationException>(() => settings.GetRequiredApiKey());
    }
}
