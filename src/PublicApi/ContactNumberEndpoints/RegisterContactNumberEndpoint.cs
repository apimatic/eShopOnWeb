using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Configuration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

/// <summary>
/// Registers a mobile number for the signed-in shopper. A number the provider does not consider a
/// usable destination is rejected here; what gets stored is the provider's canonical form.
/// </summary>
public class RegisterContactNumberEndpoint : IEndpoint<IResult, RegisterContactNumberRequest, IContactNumberService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/contact-numbers",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RegisterContactNumberRequest request, ClaimsPrincipal user, IContactNumberService service, CancellationToken ct) =>
            {
                request.CallerId = user.GetCallerId();
                return await HandleAsync(request, service, ct);
            })
            .Produces<RegisterContactNumberResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("ContactNumberEndpoints");
    }

    public Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service) =>
        HandleAsync(request, service, default);

    public async Task<IResult> HandleAsync(RegisterContactNumberRequest request, IContactNumberService service, CancellationToken ct)
    {
        var response = new RegisterContactNumberResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.CallerId))
        {
            return Results.Unauthorized();
        }

        var result = await service.RegisterAsync(request.CallerId, request.PhoneNumber ?? string.Empty, ct);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        response.ContactNumberId = result.ContactNumber!.Id;
        response.PhoneNumber = result.ContactNumber.PhoneNumber;
        return Results.Created($"api/contact-numbers/{response.ContactNumberId}", response);
    }
}

public class RegisterContactNumberRequest : BaseRequest
{
    /// <summary>The mobile number to register, in any form the provider can canonicalize.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>Set from the caller's token; never bound from the request body.</summary>
    [JsonIgnore]
    public string CallerId { get; set; } = string.Empty;
}

public class RegisterContactNumberResponse : BaseResponse
{
    public RegisterContactNumberResponse(System.Guid correlationId) : base(correlationId) { }

    public RegisterContactNumberResponse() { }

    /// <summary>The identifier of the number that was registered.</summary>
    public int ContactNumberId { get; set; }

    /// <summary>The provider's canonical E.164 form that was stored.</summary>
    public string PhoneNumber { get; set; } = string.Empty;
}
