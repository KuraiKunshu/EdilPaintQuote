using System.Security.Cryptography;
using System.Text;
using System.IO;

namespace EdilPaintPreventibiviGen.Services;

public sealed class LocalPdfOutboxService
{
    private readonly string _outboxPath;

    public LocalPdfOutboxService(string dataPath)
    {
        _outboxPath = Path.Combine(dataPath, "PendingPdfs");
        Directory.CreateDirectory(_outboxPath);
    }

    public async Task StoreAsync(string quoteNumber, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0)
            return;

        string path = BuildPath(quoteNumber);
        string temporaryPath = path + ".tmp";
        await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    public async Task<byte[]?> TryReadAsync(string quoteNumber, CancellationToken cancellationToken = default)
    {
        string path = BuildPath(quoteNumber);
        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, cancellationToken)
            : null;
    }

    public Task RemoveAsync(string quoteNumber)
    {
        string path = BuildPath(quoteNumber);
        if (File.Exists(path))
            File.Delete(path);

        return Task.CompletedTask;
    }

    private string BuildPath(string quoteNumber)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(quoteNumber));
        return Path.Combine(_outboxPath, Convert.ToHexString(hash) + ".pdf");
    }
}
