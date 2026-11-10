using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.PublicApi.Banking;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PublicApiIntegrationTests.Banking;

[TestClass]
public class BankingRulesTest
{
    [TestMethod]
    public void BaseUrl_IsDerivedFromEnvironment_UnlessOverridden()
    {
        Assert.AreEqual("https://sandbox.plaid.com", new PlaidOptions { Environment = "sandbox" }.ResolveBaseUrl());
        Assert.AreEqual("http://localhost:1234/x", new PlaidOptions { Environment = "sandbox", BaseUrl = "http://localhost:1234/x" }.ResolveBaseUrl());
    }

    [TestMethod]
    public void BusinessDays_SkipWeekendsAndHolidays()
    {
        Assert.AreEqual(new DateTime(2026, 11, 9), BusinessCalendar.OnOrAfter(new DateTime(2026, 11, 7)));
        Assert.AreEqual(new DateTime(2026, 11, 12), BusinessCalendar.OnOrAfter(new DateTime(2026, 11, 11))); // Veterans Day
        Assert.AreEqual(new DateTime(2026, 11, 30), BusinessCalendar.AddBusinessDays(new DateTime(2026, 11, 25), 2)); // Thanksgiving
    }

    [TestMethod]
    public void OptionPosition_ReportsContracts_WhateverUnitTheInstitutionUses()
    {
        // quantity in underlying shares: value = quantity x price
        Assert.AreEqual(100m, PortfolioService.ContractsHeld(new PlaidHolding { Quantity = 10000, InstitutionPrice = 0.011m, InstitutionValue = 110m }));
        // quantity already in contracts: value = quantity x price x 100
        Assert.AreEqual(3m, PortfolioService.ContractsHeld(new PlaidHolding { Quantity = 3, InstitutionPrice = 2m, InstitutionValue = 600m }));
    }

    [TestMethod]
    public void PortfolioMap_UsesContractsOnlyForOptions()
    {
        var response = new PlaidHoldingsResponse
        {
            Securities = new List<PlaidSecurity>
            {
                new() { SecurityId = "a", Type = "equity", TickerSymbol = "ABC", Name = "ABC Inc" },
                new() { SecurityId = "b", Type = "derivative", TickerSymbol = "ABC260101C00010000", Name = "ABC Call" }
            },
            Holdings = new List<PlaidHolding>
            {
                new() { SecurityId = "a", Quantity = 5, InstitutionPrice = 2, InstitutionValue = 10 },
                new() { SecurityId = "b", Quantity = 200, InstitutionPrice = 0.5m, InstitutionValue = 100 }
            }
        };
        var positions = PortfolioService.Map(response, new BankConnection()).ToList();
        Assert.AreEqual(5m, positions[0].Quantity);
        Assert.IsNull(positions[0].Contracts);
        Assert.AreEqual(2m, positions[1].Contracts);
        Assert.IsNull(positions[1].Quantity);
    }
}
