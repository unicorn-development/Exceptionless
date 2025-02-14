﻿using Exceptionless.Core.Billing;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Models;
using Exceptionless.Core.Models.Billing;
using Foundatio.Repositories.Utility;
using Foundatio.Utility;

namespace Exceptionless.Tests.Utility;

internal static class OrganizationData
{
    public static IEnumerable<Organization> GenerateOrganizations(BillingManager billingManager, BillingPlans plans, int count = 10, bool generateId = false, string? id = null)
    {
        for (int i = 0; i < count; i++)
            yield return GenerateOrganization(billingManager, plans, generateId, id);
    }

    public static List<Organization> GenerateSampleOrganizations(BillingManager billingManager, BillingPlans plans)
    {
        return new List<Organization> {
                GenerateSampleOrganization(billingManager, plans),
                GenerateOrganization(billingManager, plans, id: TestConstants.OrganizationId2, inviteEmail: TestConstants.InvitedOrganizationUserEmail),
                GenerateOrganization(billingManager, plans, id: TestConstants.OrganizationId3, inviteEmail: TestConstants.InvitedOrganizationUserEmail),
                GenerateOrganization(billingManager, plans, id: TestConstants.OrganizationId4, inviteEmail: TestConstants.InvitedOrganizationUserEmail),
                GenerateOrganization(billingManager, plans, id: TestConstants.SuspendedOrganizationId, inviteEmail: TestConstants.InvitedOrganizationUserEmail, isSuspended: true),
            };
    }

    public static Organization GenerateSampleOrganization(BillingManager billingManager, BillingPlans plans)
    {
        return GenerateOrganization(billingManager, plans, id: TestConstants.OrganizationId, name: "Acme", inviteEmail: TestConstants.InvitedOrganizationUserEmail);
    }

    public static Organization GenerateSampleOrganizationWithPlan(BillingManager billingManager, BillingPlans plans, BillingPlan plan)
    {
        return GenerateOrganization(billingManager, plans, id: TestConstants.OrganizationId, name: "Acme", inviteEmail: TestConstants.InvitedOrganizationUserEmail, plan: plan);
    }

    public static Organization GenerateOrganization(BillingManager billingManager, BillingPlans plans, bool generateId = false, string? name = null, string? id = null, string? inviteEmail = null, bool isSuspended = false, BillingPlan? plan = null)
    {
        var organization = new Organization
        {
            Id = id.IsNullOrEmpty() ? generateId ? ObjectId.GenerateNewId().ToString() : TestConstants.OrganizationId : id,
            Name = name ?? $"Organization{id}"
        };

        billingManager.ApplyBillingPlan(organization, plan ?? plans.UnlimitedPlan);
        if (organization.BillingPrice > 0)
        {
            organization.StripeCustomerId = "stripe_customer_id";
            organization.CardLast4 = "1234";
            organization.SubscribeDate = SystemClock.UtcNow;
            organization.BillingChangeDate = SystemClock.UtcNow;
            organization.BillingChangedByUserId = TestConstants.UserId;
        }

        if (!String.IsNullOrEmpty(inviteEmail))
        {
            organization.Invites.Add(new Invite
            {
                EmailAddress = inviteEmail,
                Token = Guid.NewGuid().ToString(),
                DateAdded = SystemClock.UtcNow
            });
        }

        if (isSuspended)
        {
            organization.IsSuspended = true;
            organization.SuspensionCode = SuspensionCode.Abuse;
            organization.SuspendedByUserId = TestConstants.UserId;
            organization.SuspensionDate = SystemClock.UtcNow;
        }

        return organization;
    }
}
