﻿using System.Diagnostics;
using System.Text.Json.Nodes;

namespace MelonLoader.Installer;

public static class Updater
{
    public static State CurrentState { get; private set; }
    public static string? LatestError { get; private set; }

    public static event InstallProgressEventHandler? Progress;
    public static event InstallFinishedEventHandler? Finished;

    public static bool UpdateIfPossible()
    {
        // Don't auto-update on CI builds
        if (Program.Version.Revision > 0)
            return false;

        var downloadUrl = CheckForUpdateAsync().GetAwaiter().GetResult();
        if (downloadUrl == null)
            return false;

        CurrentState = State.Updating;

        _ = UpdateAsync(downloadUrl);
        return true;
    }

    private static void Finish(string? errorMessage)
    {
        CurrentState = State.Finished;
        LatestError = errorMessage;
        Finished?.Invoke(errorMessage);
    }

    public static bool WaitAndRemoveApp(string originalPath, int prevPID)
    {
        if (!File.Exists(originalPath))
            return true;

        // Make sure we're not waiting for ourselves.
        if (prevPID == Environment.ProcessId || Path.GetFullPath(originalPath).Equals(Path.GetFullPath(Environment.ProcessPath!), StringComparison.OrdinalIgnoreCase))
            return false;

        if (prevPID != 0)
        {
            try
            {
                var prevProc = Process.GetProcessById(prevPID);

                if (!prevProc.HasExited)
                    prevProc.WaitForExit();
            }
            catch { }
        }

        try
        {
            File.Delete(originalPath);
        }
        catch
        {
            return false;
        }

        return true;
    }

    public static void HandleUpdate(string originalPath, int prevPID)
    {
        if (!WaitAndRemoveApp(originalPath, prevPID))
            return;

        File.Copy(Environment.ProcessPath!, originalPath, true);

        Process.Start(originalPath, ["-cleanup", Environment.ProcessPath!, Environment.ProcessId.ToString()]);
    }

    private static async Task UpdateAsync(string downloadUrl)
    {
        var newPath = Path.GetTempFileName() + ".exe";

        using (var newStr = File.Create(newPath))
        {
            var result = await InstallerUtils.DownloadFileAsync(downloadUrl, newStr, (progress, newStatus) => Progress?.Invoke(progress, newStatus));
            if (result != null)
            {
                Finish("Failed to download the latest installer version: " + result);
                return;
            }
        }

        Process.Start(newPath, ["-handleupdate", Environment.ProcessPath!, Environment.ProcessId.ToString()]);

        Finish(null);
    }

    public static bool CheckLegacyUpdate()
    {
        if (!Environment.ProcessPath!.EndsWith(".tmp.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        var dir = Path.GetDirectoryName(Environment.ProcessPath)!;
        var name = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
        name = name.Remove(name.Length - 4) + ".exe";

        var final = Path.Combine(dir, name);

        var prevProc = Process.GetProcessesByName(name);

        HandleUpdate(final, prevProc.FirstOrDefault()?.Id ?? 0);
        return true;
    }

    private static async Task<string?> CheckForUpdateAsync()
    {
        HttpResponseMessage response;
        try
        {
            response = await InstallerUtils.Http.GetAsync(Config.InstallerLatestReleaseApi);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
            return null;

        var respStr = await response.Content.ReadAsStringAsync();
        var json = JsonNode.Parse(respStr)!;

        if (json["tag_name"] == null)
            return null;

        var versionStr = json["tag_name"]!.ToString();
        if (versionStr.StartsWith('v'))
            versionStr = versionStr[1..];

        if (!Version.TryParse(versionStr, out var latestVer))
            return null;

        var currentVer = typeof(Updater).Assembly.GetName().Version;
        if (currentVer == null || currentVer >= latestVer)
            return null;

        var asset = json["assets"]?.AsArray().FirstOrDefault(x => x!["name"]!.ToString().EndsWith(".exe"));
        if (asset == null)
            return null;

        return asset["browser_download_url"]?.ToString();
    }

    public enum State
    {
        None,
        Updating,
        Finished
    }
}
