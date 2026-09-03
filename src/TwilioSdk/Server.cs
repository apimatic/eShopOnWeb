using TwilioSdk.Core.Models;
using TwilioSdk.Servers;

namespace TwilioSdk;

public class Server
{
    private readonly ServerEnvironment _environment;
    private readonly ServerOptions _options;

    internal Server(ServerEnvironment environment, ServerOptions options)
    {
        _environment = environment;
        _options = options;
    }

    internal UrlTemplate Default(string path) => _options.Default.Resolve(_environment, path);
    internal UrlTemplate Default1(string path) => _options.Default1.Resolve(_environment, path);
    internal UrlTemplate Default2(string path) => _options.Default2.Resolve(_environment, path);
    internal UrlTemplate Default3(string path) => _options.Default3.Resolve(_environment, path);
    internal UrlTemplate Default4(string path) => _options.Default4.Resolve(_environment, path);
    internal UrlTemplate Default5(string path) => _options.Default5.Resolve(_environment, path);
    internal UrlTemplate Default6(string path) => _options.Default6.Resolve(_environment, path);
    internal UrlTemplate Default7(string path) => _options.Default7.Resolve(_environment, path);
    internal UrlTemplate Default8(string path) => _options.Default8.Resolve(_environment, path);
    internal UrlTemplate Default9(string path) => _options.Default9.Resolve(_environment, path);
    internal UrlTemplate Default10(string path) => _options.Default10.Resolve(_environment, path);
    internal UrlTemplate Default11(string path) => _options.Default11.Resolve(_environment, path);
    internal UrlTemplate Default12(string path) => _options.Default12.Resolve(_environment, path);
    internal UrlTemplate Default13(string path) => _options.Default13.Resolve(_environment, path);
    internal UrlTemplate Default14(string path) => _options.Default14.Resolve(_environment, path);
}
