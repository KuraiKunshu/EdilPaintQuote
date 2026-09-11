using Android.Graphics;
using Android.Graphics.Pdf;
using AColor = Android.Graphics.Color;
using Path = System.IO.Path;
using Paint = Android.Graphics.Paint;
using RectF = Android.Graphics.RectF;

namespace EdilPaintPreventibiviGen.Android.Services;

public static class MobileDocumentService
{
    public static string ExportDirectory => Path.Combine(FileSystem.CacheDirectory, "exports");

    public static async Task<string> CreateAsync(string name, IReadOnlyList<DocumentLine> lines)
    {
        byte[] logo;
        await using (Stream asset = await FileSystem.OpenAppPackageFileAsync("edilpaint.png"))
        {
            using var bytes = new MemoryStream();
            await asset.CopyToAsync(bytes);
            logo = bytes.ToArray();
        }
        return await Task.Run(() => Render(name, lines, logo));
    }

    private static string Render(string name, IReadOnlyList<DocumentLine> lines, byte[] logo)
    {
        Directory.CreateDirectory(ExportDirectory);
        string fileName = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
        string path = Path.Combine(ExportDirectory, $"{fileName}-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();
        using var paint = new Paint(PaintFlags.AntiAlias);
        using var bitmap = BitmapFactory.DecodeByteArray(logo, 0, logo.Length);
        PdfDocument.Page? page = null;
        float y = 0;
        int pageNumber = 0;
        const float margin = 40, width = 515;

        void Finish()
        {
            if (page == null) return;
            paint.TextSize = 9;
            paint.Color = AColor.Gray;
            paint.SetTypeface(Typeface.Default);
            page.Canvas!.DrawText($"EdilPaint   |   {pageNumber}", margin, 816, paint);
            document.FinishPage(page);
            page.Dispose();
            page = null;
        }

        void Start()
        {
            Finish();
            pageNumber++;
            using var info = new PdfDocument.PageInfo.Builder(595, 842, pageNumber).Create();
            page = document.StartPage(info);
            if (bitmap != null)
            {
                float scale = Math.Min(130f / bitmap.Width, 44f / bitmap.Height);
                using var rect = new RectF(margin, 24, margin + bitmap.Width * scale, 24 + bitmap.Height * scale);
                page!.Canvas!.DrawBitmap(bitmap, null, rect, paint);
            }
            y = 92;
        }

        try
        {
            Start();
            foreach (DocumentLine block in lines)
            {
                float size = block.Heading ? 13 : 10;
                float lineHeight = block.Heading ? 19 : 15;
                if (block.Heading) y += 8;
                foreach (string paragraph in block.Text.Replace("\r", "").Split('\n'))
                {
                    string remaining = paragraph;
                    do
                    {
                        if (y + lineHeight > 790) Start();
                        paint.TextSize = size;
                        paint.Color = block.Heading ? AColor.Rgb(179, 38, 30) : AColor.Rgb(32, 33, 36);
                        paint.SetTypeface(block.Heading ? Typeface.DefaultBold : Typeface.Default);
                        int count = remaining.Length == 0 ? 0 : Math.Max(1, paint.BreakText(remaining, true, width, null));
                        if (count < remaining.Length)
                        {
                            int space = remaining.LastIndexOf(' ', Math.Max(0, count - 1), count);
                            if (space > 0) count = space;
                        }
                        page!.Canvas!.DrawText(remaining[..count], margin, y, paint);
                        y += lineHeight;
                        remaining = remaining[count..].TrimStart();
                    } while (remaining.Length > 0);
                }
                y += 4;
            }
            Finish();
            using var output = File.Create(path);
            document.WriteTo(output);
            return path;
        }
        finally { Finish(); }
    }

    public static void CleanExpiredExports()
    {
        if (!Directory.Exists(ExportDirectory)) return;
        foreach (string path in Directory.EnumerateFiles(ExportDirectory))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-24)) File.Delete(path);
            }
            catch (IOException) { }
        }
    }
}
