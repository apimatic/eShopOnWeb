using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's numbers. History records keep their
/// outcome but are detached, so nothing can be sent to the number again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, HttpContext>
{
    private readonly IRepository<ContactNumber> _contactNumbers;
    private readonly IRepository<OrderNotification> _notifications;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumbers, IRepository<OrderNotification> notifications)
    {
        _contactNumbers = contactNumbers;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), httpContext);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, HttpContext httpContext)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumbers.GetByIdAsync(request.ContactNumberId, httpContext.RequestAborted);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        // Detach history first: a resend of an old message must find no number to go to.
        var related = await _notifications.ListAsync(new NotificationsByContactNumberSpecification(contactNumber.Id), httpContext.RequestAborted);
        foreach (var notification in related)
        {
            notification.DetachContactNumber();
            await _notifications.UpdateAsync(notification, httpContext.RequestAborted);
        }

        await _contactNumbers.DeleteAsync(contactNumber, httpContext.RequestAborted);

        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId()));
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public DeleteContactNumberRequest(int contactNumberId)
    {
        ContactNumberId = contactNumberId;
    }

    public int ContactNumberId { get; }
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
}
