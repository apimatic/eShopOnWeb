using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.ContactNumberAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Removes one of the signed-in shopper's contact numbers. Any provider-scheduled
/// messages still addressed to it are cancelled so nothing is sent to it again.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest, ClaimsPrincipal>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;
    private readonly IOrderNotificationService _notificationService;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository,
        IOrderNotificationService notificationService)
    {
        _contactNumberRepository = contactNumberRepository;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest(contactNumberId), user);
            })
            .Produces<DeleteContactNumberResponse>()
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber is null || contactNumber.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        await _notificationService.CancelScheduledForContactNumberAsync(contactNumber.Id);
        await _contactNumberRepository.DeleteAsync(contactNumber);

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

    public string Status => "Deleted";
}
