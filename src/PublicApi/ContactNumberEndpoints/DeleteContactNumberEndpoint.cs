using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public string Status { get; set; } = "deleted";
}

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any provider-queued
/// messages to it are cancelled so it is never messaged again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(contactNumberId, user, contactNumberRepository, notificationService);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, ClaimsPrincipal user, IRepository<ContactNumber> contactNumberRepository, IOrderNotificationService notificationService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await contactNumberRepository.GetByIdAsync(contactNumberId);
        if (contactNumber == null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        // Nothing may be sent to the number again: call off provider-queued messages first.
        await notificationService.CancelScheduledMessagesForContactNumberAsync(contactNumberId);

        await contactNumberRepository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse { ContactNumberId = contactNumberId });
    }
}
