/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using WebCore.Hosting.Storage;

namespace Archivar.Web;

/// <summary>Every kind of data this app stores: its blob container, the setting that moves its local folder, and its default folder under <c>App_Data</c>. How they are reached (blob or folder) is the shared <see cref="StorageBackend"/>'s job.</summary>
internal static class AppStores
{
    public static readonly StoreSpec Users = new("users", "Storage:UsersFolder", "users");
    public static readonly StoreSpec Profiles = new("profiles", "Storage:ProfilesFolder", "profiles");
    public static readonly StoreSpec Feedback = new("feedback", "Storage:FeedbackFolder", "feedback");
    public static readonly StoreSpec SiteSettings = new("site-settings", "Storage:SiteSettingsFolder", "site-settings");
}
