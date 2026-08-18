using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. The number is validated with the provider and stored
/// in its canonical form; an unusable destination is rejected here, not when a later message fails.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, ClaimsPrincipal>
{
    private readonly IOrderNotificationService _service;

    public RegisterContactNumberEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<RegisterContactNumberResponse>(StatusCodes.Status201Created)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.CallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest("A phone number is required.");
        }

        try
        {
            var contactNumber = await _service.RegisterContactNumberAsync(buyerId, request.PhoneNumber);
            var response = new RegisterContactNumberResponse(request.CorrelationId())
            {
                ContactNumberId = contactNumber.Id,
                PhoneNumber = contactNumber.PhoneNumber
            };
            return Results.Created($"api/contact-numbers/{contactNumber.Id}", response);
        }
        catch (SmsGatewayException ex)
        {
            return ProviderErrorResults.From(ex);
        }
    }
}
