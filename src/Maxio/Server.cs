using Maxio.Core.Models;
using Maxio.Servers;

namespace Maxio;

public class Server
{
    private readonly ServerEnvironment _environment;
    private readonly ServerOptions _options;

    internal Server(ServerEnvironment environment, ServerOptions options)
    {
        _environment = environment;
        _options = options;
    }

    internal UrlTemplate Production(string path) => _options.Production.Resolve(_environment, path);
    internal UrlTemplate Ebb(string path) => _options.Ebb.Resolve(_environment, path);
    internal UrlTemplate Oauth(string path) => _options.Oauth.Resolve(_environment, path);
}
