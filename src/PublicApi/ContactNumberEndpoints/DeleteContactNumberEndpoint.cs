using System;
using System.Threading.Tasks;
using Ardalis.Result;
using IResult = Microsoft.AspNetCore.Http.IResult;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }
    public string Status { get; set; } = "deleted";
}

/// <summary>
/// Removes one of the caller's numbers. Scoped to the owner: one shopper can never delete another's.
/// Afterwards the number no longer appears among the caller's numbers and nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext http, IContactNumberService service) =>
            {
                var buyerId = http.User.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                return await HandleAsync(new DeleteContactNumberRequest { ContactNumberId = contactNumberId, BuyerId = buyerId }, service);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service)
    {
        var result = await service.DeleteAsync(request.BuyerId, request.ContactNumberId);
        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound();
        }
        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
