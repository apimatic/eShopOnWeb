using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The number the caller typed. The provider's canonical form of it is what gets stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the caller's token; any value sent by the caller is ignored.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }

    public int ContactNumberId { get; set; }
    public string CanonicalNumber { get; set; } = string.Empty;
    public DateTimeOffset RegisteredAt { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here (400); what gets stored is the provider's canonical form of the number.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ISmsNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, ISmsNotificationService service) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ISmsNotificationService service)
    {
        var result = await service.RegisterContactNumberAsync(request.BuyerId, request.PhoneNumber);
        if (result.Rejected)
            return Results.BadRequest(new { message = result.RejectionReason });

        var number = result.Number!;
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = number.Id,
            CanonicalNumber = number.CanonicalNumber,
            RegisteredAt = number.RegisteredAt
        };
        return Results.Created($"api/contact-numbers/{number.Id}", response);
    }
}
