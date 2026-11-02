namespace EdilPaintPreventibiviGen.Models;

public sealed class WorkSheetSettingsModel
{
    public int PhotosPerPage { get; set; } = 1;

    public int GetEffectivePhotosPerPage() => PhotosPerPage == 2 ? 2 : 1;

    public void Normalize() => PhotosPerPage = GetEffectivePhotosPerPage();
}
