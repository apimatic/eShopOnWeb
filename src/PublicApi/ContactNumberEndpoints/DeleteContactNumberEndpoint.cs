using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Once removed it is never
/// sent to again: sends resolve the shopper's registered numbers at send time, and
/// operator resends verify the destination is still registered.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, IRepository<ContactNumber>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, IRepository<ContactNumber> contactNumberRepository) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId) { BuyerId = httpContext.User.Identity?.Name }, contactNumberRepository);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, IRepository<ContactNumber> contactNumberRepository)
    {
        var response = new DeleteContactNumberResponse(request.CorrelationId());

        var contactNumber = await contactNumberRepository.GetByIdAsync(request.ContactNumberId);

        // A shopper must never see (or remove) another shopper's number.
        if (contactNumber == null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound(response);
        }

        await contactNumberRepository.DeleteAsync(contactNumber);
        response.Result = true;
        return Results.Ok(response);
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
    public string? BuyerId { get; set; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public bool Result { get; set; }
}
