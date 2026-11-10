using System;
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
    public int? Count { get; init; }
    public int? Offset { get; init; }
}

public class ListBanksResponse : BaseResponse
{
    public ListBanksResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListBanksResponse()
    {
    }

    public List<BankDto> Banks { get; set; } = new();
    public int Total { get; set; }
}

/// <summary>
/// The checkout's bank picker: the banks Plaid supports, searchable by name
/// </summary>
public class BankListEndpoint : IEndpoint<IResult, ListBanksRequest, BankPickerService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/banks",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? query, int? count, int? offset, BankPickerService picker) =>
            {
                return await HandleAsync(new ListBanksRequest { Query = query, Count = count, Offset = offset }, picker);
            })
            .Produces<ListBanksResponse>()
            .WithTags("BankEndpoints");
    }

    public async Task<IResult> HandleAsync(ListBanksRequest request, BankPickerService picker)
    {
        var result = await picker.ListAsync(request.Query, request.Count, request.Offset);
        return Results.Ok(new ListBanksResponse(request.CorrelationId()) { Banks = result.Banks, Total = result.Total });
    }
}
