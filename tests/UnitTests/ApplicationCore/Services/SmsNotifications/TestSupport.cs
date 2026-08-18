using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.UnitTests.ApplicationCore.Services.SmsNotifications;

/// <summary>Test helpers for the SMS-notification services.</summary>
internal static class TestSupport
{
    /// <summary>Sets the EF-assigned <see cref="BaseEntity.Id"/> that a mocked repository would not populate.</summary>
    public static T WithId<T>(this T entity, int id) where T : BaseEntity
    {
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.Id))!.SetValue(entity, id);
        return entity;
    }
}
