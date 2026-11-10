using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public record BankDto(string Id, string Name, string? Url, string? Logo, string? PrimaryColor, bool Oauth);

public class ListBanksResponse : BaseResponse
{
    public List<BankDto> Banks { get; set; } = new();
    /// <summary>How many banks match in all (not just on this page).</summary>
    public int Total { get; set; }
    public int Offset { get; set; }
}

/// <summary>
/// The checkout bank picker: the banks Plaid supports, optionally searched by name
/// </summary>
public class BankListEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, int? count, int? offset, InstitutionService institutions, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(query, count, offset, institutions, cancellationToken);
            })
            .Produces<ListBanksResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(string? query, int? count, int? offset, InstitutionService institutions, CancellationToken cancellationToken)
    {
        var page = await institutions.ListAsync(query, count, offset, cancellationToken);
        var response = new ListBanksResponse
        {
            Banks = page.Banks.Select(b => new BankDto(b.Id, b.Name, b.Url, b.Logo, b.PrimaryColor, b.Oauth)).ToList(),
        };
        response.Total = page.Total;
        response.Offset = offset ?? 0;
        return Results.Ok(response);
    }
}
