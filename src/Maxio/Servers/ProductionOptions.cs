using Maxio.Core.Models;

namespace Maxio.Servers;

public class ProductionOptions
{
    public UsOptions Us { get; set; } = new();
    public EuOptions Eu { get; set; } = new();
    public MaxioApiGatewayOptions MaxioApiGateway { get; set; } = new();

    internal UrlTemplate Resolve(ServerEnvironment environment, string path) =>
        environment.Match(() => new UrlTemplate(Us.BaseUrl,
                path,
                [TemplateParam.ForServer("site", Us.Site)]),
            () => new UrlTemplate(Eu.BaseUrl, path, [TemplateParam.ForServer("site", Eu.Site)]),
            () => new UrlTemplate(MaxioApiGateway.BaseUrl,
                path,
                [TemplateParam.ForServer("connector", MaxioApiGateway.Connector)]));

    public class UsOptions
    {
        public string BaseUrl { get; set; } = "https://{site}.chargify.com";
        public string Site { get; set; } = "subdomain";
    }

    public class EuOptions
    {
        public string BaseUrl { get; set; } = "https://{site}.ebilling.maxio.com";
        public string Site { get; set; } = "subdomain";
    }

    public class MaxioApiGatewayOptions
    {
        public string BaseUrl { get; set; } = "https://{connector}.api.maxio.com/api/v1/billing";
        public string Connector { get; set; } = "connector";
    }
}
