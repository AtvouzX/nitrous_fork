using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using System.IO;

namespace Nitrous.Managers;

public static class UpdateManager
{
    public const string CurrentVersion = "0.8.11";
    private const string GithubRepo = "AtvouzX/nitrous_fork";

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
                    if (MessageBox.Show($"New version ({latestTag}) is available! Update now?", "Update",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        await PerformUpdateAsync(latestTag, exitCallback);
                    }
                }
                else if (!silent)
                {
                    MessageBox.Show($"Nitrous is up to date! ({CurrentVersion})", "Up to date", MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox.Show($"Update check failed: {ex.Message}", "Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        }
    }

    private static async Task PerformUpdateAsync(string tag, Action exitCallback)
    {
        try
        {
            // Validate tag to prevent path manipulation
            if (string.IsNullOrWhiteSpace(tag) || tag.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                tag.Contains(".."))
            {
                MessageBox.Show("Invalid release tag received.", "Update Error", MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            string dlUrl = $"https://github.com/{GithubRepo}/releases/download/{tag}/Nitrous.exe";
            string tempExe = Path.Combine(Path.GetTempPath(), $"Nitrous_update_{Guid.NewGuid():N}.exe");
            string currentExe = Application.ExecutablePath;

            using var client = CreateHttpClient();
            byte[] data = await client.GetByteArrayAsync(dlUrl);
            if (data.Length < 1024)
            {
                throw new InvalidDataException("Downloaded update payload is corrupt or incomplete.");
            }

            await File.WriteAllBytesAsync(tempExe, data);

            // Escape paths for cmd.exe invocation
            string sanitizedTemp = tempExe.Replace("\"", "");
            string sanitizedCurrent = currentExe.Replace("\"", "");
            string cmd =
                $"/c timeout /t 2 /nobreak & move /y \"{sanitizedTemp}\" \"{sanitizedCurrent}\" & start \"\" \"{sanitizedCurrent}\"";

            using var p = Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                { CreateNoWindow = true, UseShellExecute = false });

            exitCallback.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
