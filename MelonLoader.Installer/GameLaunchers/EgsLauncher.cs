﻿using Microsoft.Win32;
using System.Text.Json.Nodes;

namespace MelonLoader.Installer.GameLaunchers;

public class EgsLauncher : GameLauncher
{
    private static readonly string? manifestDir;

    static EgsLauncher()
    {
        manifestDir = Registry.CurrentUser.OpenSubKey(@"Software\Epic Games\EOS")?.GetValue("ModSdkMetadataDir") as string;
        if (manifestDir != null && !Directory.Exists(manifestDir))
            manifestDir = null;
    }

    internal EgsLauncher() : base("/Assets/egs.png") { }

    public override void AddGames()
    {
        if (manifestDir == null)
            return;

        foreach (var item in Directory.EnumerateFiles(manifestDir, "*.item"))
        {
            var json = JsonNode.Parse(File.ReadAllText(item));
            if (json == null || (bool?)json["bIsExecutable"] != true)
                continue;

            var dir = (string?)json["InstallLocation"];
            if (dir == null || !Directory.Exists(dir))
                continue;

            var name = (string?)json["DisplayName"];
            if (name == null)
                continue;

            GameManager.TryAddGame(dir, name, this, null, out _);
        }
    }
}