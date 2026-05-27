using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EdilPaintPreventibiviGen.Models;
using EdilPaintPreventibiviGen.Services;
using EdilPaintPreventibiviGen.Views;

namespace EdilPaintPreventibiviGen.ViewModels;
public partial class MainViewModel
{
    #region Paths / Config
    private string GetAssetsPath()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidates =
        [
            Path.Combine(baseDir, "Assets"),
            Path.Combine(baseDir, "assets"),
            Path.Combine(baseDir, "..", "..", "..", "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "assets")
        ];
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private void LoadSignificantMaterialsConfig(string assetsPath)
    {
        _significantMaterialPrefixes.Clear();
        string configPath = Path.Combine(assetsPath, "significant_materials.json");
        if (!File.Exists(configPath)) return;

        try
        {
            string json = File.ReadAllText(configPath);
            var prefixes = JsonSerializer.Deserialize<List<string>>(json);
            if (prefixes == null) return;
            foreach (var prefix in prefixes)
                if (!string.IsNullOrWhiteSpace(prefix))
                    _significantMaterialPrefixes.Add(prefix.Trim());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadSignificantMaterialsConfig] Error: {ex.Message}");
        }
    }

    private bool IsMaterialSignificant(string? materialName)
    {
        if (string.IsNullOrWhiteSpace(materialName)) return false;
        return _significantMaterialPrefixes.Any(prefix =>
            materialName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
    #endregion
}

