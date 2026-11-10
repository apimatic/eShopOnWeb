using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Banking;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.BankEndpoints;

public class ListBanksRequest : BaseRequest
{
    public string? Query { get; init; }
}

public class ListBanksResponse : BaseResponse
{
    public ListBanksResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<BankInfo> Banks { get; set; } = new List<BankInfo>();
}

/// <summary>
/// The checkout's bank picker: the banks Plaid supports, optionally searched by name
/// </summary>
public class BankListEndpoint : IEndpoint<IResult, ListBanksRequest, InstitutionDirectory>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, InstitutionDirectory directory) =>
            {
                return await HandleAsync(new ListBanksRequest { Query = query }, directory);
            })
            .Produces<ListBanksResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListBanksRequest request, InstitutionDirectory directory)
    {
        var response = new ListBanksResponse(request.CorrelationId())
        {
            Banks = await directory.SearchAsync(request.Query, default),
        };
        return Results.Ok(response);
    }
}
