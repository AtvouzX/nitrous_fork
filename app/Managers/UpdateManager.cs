using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.IO;

namespace Nitrous.Managers;

public static class UpdateManager
{
    public const string CurrentVersion = "0.9.13";
    private const string GithubRepo = "AtvouzX/nitrous_fork";
    public static readonly string CachedCmdPath = Path.Combine(Environment.SystemDirectory, "cmd.exe");

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NitrousApp/1.0");
        return client;
    }

    public static async Task CheckForUpdatesAsync(bool silent, Action exitCallback)
    {
        try
        {
            LogManager.Info("Checking for Nitrous updates via GitHub API...", "Update");
            using var client = CreateHttpClient();
            string res = await client.GetStringAsync($"https://api.github.com/repos/{GithubRepo}/releases/latest");
            using var doc = JsonDocument.Parse(res);
            string latestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";

            string cleanLatest = latestTag.Trim().TrimStart('v', 'V');
            string cleanCurrent = CurrentVersion.Trim().TrimStart('v', 'V');

            if (Version.TryParse(cleanLatest, out Version? vLatest) &&
                Version.TryParse(cleanCurrent, out Version? vCurrent))
            {
                if (vLatest > vCurrent)
                {
                    LogManager.Info($"New Nitrous release available: {latestTag} (Current: {CurrentVersion})", "Update");
                    if (MessageBox.Show($"New version ({latestTag}) is available! Update now?", "Update",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        await PerformUpdateAsync(latestTag, exitCallback);
                    }
                }
                else
                {
                    LogManager.Info($"Nitrous is up to date ({CurrentVersion}).", "Update");
                    if (!silent)
                    {
                        MessageBox.Show($"Nitrous is up to date! ({CurrentVersion})", "Up to date", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Warn($"Update check failed: {ex.Message}", "Update");
            if (!silent)
                MessageBox.Show($"Update check failed: {ex.Message}", "Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        }
    }

    private static async Task PerformUpdateAsync(string tag, Action exitCallback)
    {
        try
        {
            LogManager.Info($"Performing update to version {tag}...", "Update");
            // Validate tag to prevent path manipulation
            if (string.IsNullOrWhiteSpace(tag) || tag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                tag.Contains(".."))
            {
                LogManager.Error($"Invalid release tag received: '{tag}'", null, "Update");
                MessageBox.Show("Invalid release tag received.", "Update Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string dlUrl = $"https://github.com/{GithubRepo}/releases/download/{tag}/Nitrous.exe";
            string tempExe = Path.Combine(Path.GetTempPath(), $"Nitrous_update_{Guid.NewGuid():N}.exe");
            string currentExe = Application.ExecutablePath;
            string backupExe = currentExe + ".old";

            using var client = CreateHttpClient();
            byte[] data = await client.GetByteArrayAsync(dlUrl);
            if (data.Length < 1024)
            {
                throw new InvalidDataException("Downloaded update payload is corrupt or incomplete.");
            }

            // Verify Windows PE executable header ('M', 'Z')
            if (data[0] != (byte)'M' || data[1] != (byte)'Z')
            {
                throw new InvalidDataException("Downloaded file is not a valid Windows executable.");
            }

            await File.WriteAllBytesAsync(tempExe, data);
            LogManager.Info($"Update binary downloaded successfully ({data.Length} bytes). Performing in-process replacement.", "Update");

            try
            {
                if (File.Exists(backupExe)) File.Delete(backupExe);

                // Windows permits renaming a running executable, but not overwriting it in place
                File.Move(currentExe, backupExe);

                try
                {
                    File.Copy(tempExe, currentExe, overwrite: true);
                }
                catch
                {
                    // Roll back so the application is preserved if copy fails
                    try
                    {
                        if (File.Exists(currentExe)) File.Delete(currentExe);
                        File.Move(backupExe, currentExe);
                    }
                    catch (Exception rollbackEx)
                    {
                        LogManager.Error($"Rollback failed: {rollbackEx.Message}", rollbackEx, "Update");
                    }

                    throw;
                }

                try
                {
                    if (File.Exists(tempExe)) File.Delete(tempExe);
                }
                catch { }

                exitCallback.Invoke();
                Program.ReleaseAppMutex();
                Process.Start(new ProcessStartInfo(currentExe, "--updated") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogManager.Error($"Update installation failed: {ex.Message}", ex, "Update");
                MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            LogManager.Error($"Update download failed: {ex.Message}", ex, "Update");
            MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Removes the previous executable (.old) left behind by an in-place update.</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            var oldExe = Application.ExecutablePath + ".old";
            if (File.Exists(oldExe)) File.Delete(oldExe);
        }
        catch (Exception ex)
        {
            LogManager.Debug($"CleanupOldFiles: could not remove old executable: {ex.Message}", "Update");
        }
    }
}
