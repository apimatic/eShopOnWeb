using System;
using System.Text.Json.Serialization;
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

public class DeleteContactNumberRequest : BaseRequest
{
    public int ContactNumberId { get; set; }

    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class DeleteContactNumberResponse : BaseResponse
{
    public DeleteContactNumberResponse(Guid correlationId) : base(correlationId) {}
    public DeleteContactNumberResponse() {}

    public int ContactNumberId { get; set; }
    public string Status { get; set; } = "Deleted";
}

/// <summary>
/// Removes one of the signed-in shopper's registered contact numbers. Once removed,
/// nothing is sent to that number again.
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
            (int contactNumberId, HttpContext httpContext) =>
            {
                var request = new DeleteContactNumberRequest
                {
                    ContactNumberId = contactNumberId,
                    BuyerId = httpContext.User.Identity?.Name ?? string.Empty
                };
                return await HandleAsync(request);
            })
            .Produces<DeleteContactNumberResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(DeleteContactNumberRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var contactNumber = await _contactNumberRepository.GetByIdAsync(request.ContactNumberId);
        if (contactNumber == null || contactNumber.BuyerId != request.BuyerId)
        {
            return Results.NotFound(new { message = $"Contact number {request.ContactNumberId} was not found." });
        }

        await _contactNumberRepository.DeleteAsync(contactNumber);

        var response = new DeleteContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = request.ContactNumberId
        };

        return Results.Ok(response);
    }
}
