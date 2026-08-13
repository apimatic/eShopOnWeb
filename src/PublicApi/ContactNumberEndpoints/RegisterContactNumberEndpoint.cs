using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a usable
/// destination is rejected here; what gets stored is the provider's own canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<RegisterContactNumberResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { error = "A phone number is required." });
        }

        var result = await service.RegisterAsync(request.BuyerId, request.PhoneNumber);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        var response = new RegisterContactNumberResponse { ContactNumberId = result.ContactNumberId };
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class RegisterContactNumberRequest
{
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Set server-side from the caller's token; never bound from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }
}
