using System;
using System.Security.Claims;
using System.Text.Json.Serialization;
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
/// Removes one of the signed-in shopper's contact numbers. Once removed it is never
/// sent to again. Another shopper's number is treated as not found.
/// </summary>
public class DeleteContactNumberEndpoint : IEndpoint<IResult, DeleteContactNumberRequest>
{
    private readonly IRepository<ContactNumber> _contactNumberRepository;

    public DeleteContactNumberEndpoint(IRepository<ContactNumber> contactNumberRepository)
    {
        _contactNumberRepository = contactNumberRepository;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapDelete("api/contact-numbers/{contactNumberId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int contactNumberId, ClaimsPrincipal user) =>
            {
                return await HandleAsync(new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = user.Identity?.Name ?? string.Empty
                });
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request)
    {
        var contactNumber = await _contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber is null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound();
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);

        return Results.Ok(new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId,
            Deleted = true
        });
    }
}

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public DeleteContactNumberResponse() { }

    public int ContactNumberId { get; set; }
    public bool Deleted { get; set; }
}
