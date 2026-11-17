/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Archivar.Core.Accounts;
using Archivar.Core.Email;
using WebCore.Accounts;
using WebCore.Email;
using WebCore.Hosting.Accounts;
using WebCore.Hosting.Storage;

namespace Archivar.Web.Accounts;

/// <summary>
/// Archivar's part of the shared accounts module (WebCore docs/accounts.md): its email provider, its Member/Admin roles, its stores, and the names its
/// live cookies, claims and logs use. Everything else (sign-in, codes, passwords, the cookie checks, the form endpoints) is WebCore's.
/// See docs/accounts.md.
/// </summary>
public static class AccountServices
{
    public static WebApplicationBuilder AddAccounts(this WebApplicationBuilder builder, StorageBackend storage)
    {
        var config = builder.Configuration;

        // Email: Azure Communication Services when Email:AzureConnectionString and Email:FromEmail are both set; otherwise WebCore's sender that only logs.
        var emailConfigured = IsEmailConfigured(config);
        if (emailConfigured)
        {
            builder.Services.AddSingleton<IAzureEmailTransport>(_ => new AzureEmailTransport(config["Email:AzureConnectionString"]!));
            builder.Services.AddSingleton<IEmailSender>(sp => new AzureEmailSender(sp.GetRequiredService<IAzureEmailTransport>(), config["Email:FromEmail"]!));
        }

        // These names are read back from live cookies, logs and storage: changing one signs everybody out, or hides their accounts.
        builder.AddWebCoreAccounts(o =>
        {
            o.BrandName = "Archivar";
            o.Roles = Roles.All;
            o.UsersStore = storage.Documents(AppStores.Users);
            o.ProfilesStore = storage.Documents(AppStores.Profiles);
            o.EmailDeliveryConfigured = emailConfigured;
            o.CookieName = "archivar_auth";
            o.CodeClientCookieName = "archivar_code_client";
            o.ClaimPrefix = "archivar:";
            o.LogCategory = "Archivar.Accounts";
            // The home page (Archivar has no /wave page; the old redirect for admins was a 404, as it was in CrateDigger).
            o.MasqueradeLandingPath = "/";
        });
        builder.Services.AddCascadingAuthenticationState();
        return builder;
    }

    /// <summary>True when real email delivery (Azure Communication Services) is configured. Shared with the alert-email logging sink.</summary>
    public static bool IsEmailConfigured(IConfiguration config) =>
        !string.IsNullOrWhiteSpace(config["Email:AzureConnectionString"]) && !string.IsNullOrWhiteSpace(config["Email:FromEmail"]);
}
