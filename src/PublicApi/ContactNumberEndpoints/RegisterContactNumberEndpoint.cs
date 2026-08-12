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
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set from the caller's token, not the request body.</summary>
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
/// usable destination is rejected here; what is stored is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, IContactNumberService service, ClaimsPrincipal user) =>
            {
                request.BuyerId = CallerIdentity.GetUserName(user);
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var result = await service.RegisterAsync(request.BuyerId, request.PhoneNumber);
        if (!result.Succeeded)
        {
            return Results.UnprocessableEntity(new { error = result.Error });
        }

        var response = new RegisterContactNumberResponse(request.CorrelationId())
        {
            ContactNumberId = result.ContactNumberId
        };
        return Results.Created($"api/contact-numbers/{result.ContactNumberId}", response);
    }
}
