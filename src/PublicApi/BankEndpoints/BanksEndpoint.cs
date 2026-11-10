using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

/// <summary>The checkout's bank picker: banks Plaid supports, searchable by name.</summary>
public class BanksEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
                [Authorize(AuthenticationSchemes = Shopper.Scheme)] async (string? query, int? limit, int? offset,
                    BankDirectoryService banks) => Results.Ok(await banks.GetBanksAsync(query, limit, offset)))
            .WithTags("BankEndpoints");
    }
}
