using PayPalServerSdk.Core.Models;

namespace PayPalServerSdk.Servers;

public class DefaultOptions
{
    public SandboxOptions Sandbox { get; set; } = new();

    internal UrlTemplate Resolve(ServerEnvironment environment, string path) =>
        environment.Match(() => new UrlTemplate(Sandbox.BaseUrl, path, []));

    public class SandboxOptions
    {
        public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";
    }
}
