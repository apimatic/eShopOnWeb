using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
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
    public int ContactNumberId { get; }
    public string BuyerId { get; }

    public DeleteContactNumberRequest(int contactNumberId, string buyerId)
    {
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
    }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
}

/// <summary>
/// Removes one of the caller's numbers. Afterwards it no longer appears among the caller's numbers
/// and nothing is sent to it again. A number that is not the caller's is treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IContactNumberService, CancellationToken>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IContactNumberService service, CancellationToken cancellationToken) =>
                await HandleAsync(new DeleteContactNumberRequest(contactNumberId, user.GetBuyerId()), service, cancellationToken))
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IContactNumberService service, CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(request.ContactNumberId, request.BuyerId, cancellationToken);
        if (!deleted)
            return Results.NotFound();

        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}
