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
    /// <summary>The number the caller typed; the provider's canonical form of it is what gets stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set server-side from the caller's token; never taken from the request body.</summary>
    public string? BuyerId { get; set; }
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
}

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here (HTTP 400) rather than when a later message fails.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.BuyerId = user.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var contactNumberId = await service.RegisterAsync(request.BuyerId, request.PhoneNumber);
        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = contactNumberId
        };
        return Results.Created($"api/contact-numbers/{contactNumberId}", response);
    }
}
