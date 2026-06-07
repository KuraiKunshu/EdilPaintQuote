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
    #region Attachments
    public async Task AddAttachmentFromPathAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return;

        string fileName = Path.GetFileName(filePath);
        if (AttachedImages.Any(x =>
                x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            return;

        byte[] content;
        try
        {
            content = await File.ReadAllBytesAsync(filePath, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"[ATTACHMENT] File non leggibile: {filePath} -> {ex.Message}");
            return;
        }

        if (AttachedImages.Any(x =>
                x.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase) ||
                x.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
            return;

        AttachedImages.Add(new SelectedAttachment
        {
            FileName = fileName,
            FilePath = filePath,
            ContentType = GetContentType(filePath),
            Content = content
        });
    }

    public void RemoveAttachment(SelectedAttachment attachment) => AttachedImages.Remove(attachment);

    private static string GetContentType(string filePath)
    {
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => "application/pdf",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".bmp" => "image/bmp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
    #endregion
}

