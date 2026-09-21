using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>Registers a mobile number for the signed-in shopper.</summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                request.BuyerId = CallerIdentity.GetBuyerId(user);
                return await HandleAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service)
        => HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
            return Results.BadRequest(new { message = "A phone number is required." });

        var contactNumber = await service.RegisterAsync(request.BuyerId, request.PhoneNumber, ct);

        return Results.Created($"api/contact-numbers/{contactNumber.Id}", new RegisterContactNumberResponse
        {
            ContactNumberId = contactNumber.Id,
            E164Number = contactNumber.E164Number
        });
    }
}

public class RegisterContactNumberRequest
{
    /// <summary>The mobile number to register, in any format the provider can canonicalize.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Set from the caller's token; not supplied by the client.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? BuyerId { get; set; }
}

public class RegisterContactNumberResponse
{
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical (E.164) form of the number that was stored.</summary>
    public string E164Number { get; set; } = string.Empty;
}
