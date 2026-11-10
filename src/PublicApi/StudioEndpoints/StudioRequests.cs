using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.eShopWeb.PublicApi.Studio;

namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>Body of a shopper's request to book a place in a class.</summary>
public class BookClassBody : NewClientDetails
{
    public int? ClassId { get; set; }
}

/// <summary>Body of a shopper's request to book a one-to-one session.</summary>
public class BookAppointmentBody : NewClientDetails
{
    public int? SessionTypeId { get; set; }

    /// <summary>ISO-8601 date-time with its UTC offset.</summary>
    public string? StartTime { get; set; }

    /// <summary>Optional: a specific trainer; otherwise any trainer who is free.</summary>
    public long? TrainerId { get; set; }
}

/// <summary>Body of a front-desk request to place a shopper in a class.</summary>
public class AdminBookClassBody : NewClientDetails
{
    /// <summary>The shopper's eShop user name (their email).</summary>
    public string? Shopper { get; set; }
    public int? ClassId { get; set; }
}

/// <summary>A request plus the caller's identity, which only ever comes from the JWT.</summary>
public class StudioRequest<TBody> : BaseRequest
{
    public StudioRequest(ClaimsPrincipal? user, TBody body)
    {
        UserName = user?.Identity?.Name;
        Body = body;
    }

    public string? UserName { get; }
    public TBody Body { get; }
}

public record ListClassesQuery(string? From, string? To);

public record ListSlotsQuery(int? SessionTypeId, string? From, string? To);

public class ListClassesResponse : BaseResponse
{
    public List<ClassDto> Classes { get; set; } = new();
}

public class ListSlotsResponse : BaseResponse
{
    public int SessionTypeId { get; set; }
    public List<SlotDto> Slots { get; set; } = new();
}

public class ListTrainersResponse : BaseResponse
{
    public List<TrainerDto> Trainers { get; set; } = new();
}
