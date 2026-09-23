using TwilioSdk.Core.Models;

namespace TwilioSdk.Servers;

public class Default13Options
{
    public ProductionOptions Production { get; set; } = new();

    internal UrlTemplate Resolve(ServerEnvironment environment, string path) =>
        environment.Match(() => new UrlTemplate(Production.BaseUrl, path, []));

    public class ProductionOptions
    {
        public string BaseUrl { get; set; } = "https://flex-api.twilio.com";
    }
}
