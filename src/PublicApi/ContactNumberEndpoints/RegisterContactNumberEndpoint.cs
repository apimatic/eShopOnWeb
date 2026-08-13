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
    /// <summary>The raw number the caller typed; the provider's canonical form is what gets stored.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Owning shopper — set from the token, never trusted from the request body.</summary>
    public string BuyerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(Guid correlationId) : base(correlationId) { }
    public RegisterContactNumberResponse() { }

    public int ContactNumberId { get; set; }
}

/// <summary>
/// POST /api/contact-numbers — registers a mobile number for the signed-in shopper. A number the
/// provider does not consider a usable destination is rejected here (400).
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Number))
            return Results.BadRequest("A number is required.");

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = await service.RegisterAsync(request.BuyerId, request.Number)
        };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}
