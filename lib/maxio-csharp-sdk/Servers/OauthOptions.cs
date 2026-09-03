using Maxio.Core.Models;

namespace Maxio.Servers;

public class OauthOptions
{
    public UsOptions Us { get; set; } = new();
    public EuOptions Eu { get; set; } = new();
    public MaxioApiGatewayOptions MaxioApiGateway { get; set; } = new();

    internal UrlTemplate Resolve(ServerEnvironment environment, string path) =>
        environment.Match(() => new UrlTemplate(Us.BaseUrl,
                path,
                [TemplateParam.ForServer("connector", Us.Connector)]),
            () => new UrlTemplate(Eu.BaseUrl, path, [TemplateParam.ForServer("connector", Eu.Connector)]),
            () => new UrlTemplate(MaxioApiGateway.BaseUrl,
                path,
                [TemplateParam.ForServer("connector", MaxioApiGateway.Connector)]));

    public class UsOptions
    {
        public string BaseUrl { get; set; } = "https://{connector}.api.maxio.com";
        public string Connector { get; set; } = "connector";
    }

    public class EuOptions
    {
        public string BaseUrl { get; set; } = "https://{connector}.api.maxio.com";
        public string Connector { get; set; } = "connector";
    }

    public class MaxioApiGatewayOptions
    {
        public string BaseUrl { get; set; } = "https://{connector}.api.maxio.com";
        public string Connector { get; set; } = "connector";
    }
}
