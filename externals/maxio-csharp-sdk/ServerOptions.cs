using Maxio.Servers;

namespace Maxio;

public class ServerOptions
{
    public ProductionOptions Production { get; set; } = new();
    public EbbOptions Ebb { get; set; } = new();
    public OauthOptions Oauth { get; set; } = new();
}
