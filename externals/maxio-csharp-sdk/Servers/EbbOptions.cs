using Maxio.Core.Models;

namespace Maxio.Servers;

public class EbbOptions
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
                [TemplateParam.ForServer("site", MaxioApiGateway.Site)]));

    public class UsOptions
    {
        public string BaseUrl { get; set; } = "https://events.chargify.com/{site}";
        public string Site { get; set; } = "subdomain";
    }

    public class EuOptions
    {
        public string BaseUrl { get; set; } = "https://events.chargify.com/{site}";
        public string Site { get; set; } = "subdomain";
    }

    public class MaxioApiGatewayOptions
    {
        public string BaseUrl { get; set; } = "https://events.chargify.com/{site}";
        public string Site { get; set; } = "subdomain";
    }
}
