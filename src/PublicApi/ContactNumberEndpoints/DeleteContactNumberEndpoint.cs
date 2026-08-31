using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any message still queued
/// with the provider for that number is called off, so it is never messaged again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IOrderNotificationService _orderNotificationService;

    public DeleteContactNumberEndpoint(
        IRepository<ContactNumber> contactNumberRepository,
        IOrderNotificationService orderNotificationService)
    {
        _contactNumberRepository = contactNumberRepository;
        _orderNotificationService = orderNotificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, HttpContext httpContext, CancellationToken ct) =>
            {
                return await HandleAsync(contactNumberId, httpContext, ct);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(int contactNumberId, HttpContext httpContext, CancellationToken ct)
    {
        var buyerId = httpContext.User.GetBuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(contactNumberId, ct);

        // Another shopper's number is indistinguishable from one that does not exist.
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        // Call off anything still queued with the provider for this number first,
        // then remove the number so nothing new can be addressed to it.
        await _orderNotificationService.CancelScheduledMessagesToNumberAsync(buyerId, contactNumber.PhoneNumber, ct);
        await _contactNumberRepository.DeleteAsync(contactNumber, ct);

        var response = new DeleteContactNumberResponse(Guid.NewGuid())
        {
            ContactNumberId = contactNumberId
        };
        return Results.Ok(response);
    }
}
