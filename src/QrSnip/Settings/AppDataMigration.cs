using System;
using System.IO;

namespace QrSnip.Settings;

// One-time migration: rename %APPDATA%\QrSnip\ → %APPDATA%\QRSnapper\
// and %LOCALAPPDATA%\QrSnip\ → %LOCALAPPDATA%\QRSnapper\. Run once at
// startup before any reads of the new paths happen.
//
// The old "QrSnip" name was the project's internal codename before we
// adopted the user-facing brand "QR Snapper". The folders were left alone
// during the rename to avoid risking settings loss; now that we have
// migration safety in place, move them so what users see in AppData
// matches what they see in the tray.
//
// Safety rules:
//   - If only the new folder exists: no-op.
//   - If only the old folder exists: rename it to the new name.
//   - If both exist: leave them alone and log. (Probably means the user
//     re-installed or the migration was interrupted. Don't risk merging.)
//   - If neither exists: no-op (the SettingsService / Diagnostics paths
//     create the new folder on first use).
internal static class AppDataMigration
{
    public const string OldFolderName = "QrSnip";
    public const string NewFolderName = "QRSnapper";

    public static void Run()
    {
        Migrate(Environment.SpecialFolder.ApplicationData);       // config.json
        Migrate(Environment.SpecialFolder.LocalApplicationData);  // startup.log
    }

    private static void Migrate(Environment.SpecialFolder folder)
    {
        var root = Environment.GetFolderPath(folder);
        var oldPath = Path.Combine(root, OldFolderName);
        var newPath = Path.Combine(root, NewFolderName);

        if (!Directory.Exists(oldPath))
        {
            // Either nothing to migrate, or migration already happened.
            return;
        }

        if (Directory.Exists(newPath))
        {
            // Both exist. Don't risk a merge — log and bail. The user has
            // both folders for some reason (maybe an older + newer instance
            // ran side by side); they can sort it out manually.
            Diagnostics.Log(
                $"AppDataMigration: both '{oldPath}' and '{newPath}' exist; " +
                $"leaving old folder in place. Merge manually if you want " +
                $"the contents of the old folder.");
            return;
        }

        try
        {
            Directory.Move(oldPath, newPath);
            Diagnostics.Log($"AppDataMigration: moved '{oldPath}' -> '{newPath}'");
        }
        catch (Exception ex)
        {
            // Don't crash the app — running with the old folder still present
            // just means future writes go to the new folder while old data
            // sits orphaned. The user can fix by hand. We log so support has
            // a breadcrumb.
            Diagnostics.LogException("AppDataMigration.Move", ex);
        }
    }
}
