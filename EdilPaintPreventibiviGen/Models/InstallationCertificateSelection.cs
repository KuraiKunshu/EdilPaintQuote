using System.ComponentModel;

namespace EdilPaintPreventibiviGen.Models;

public sealed class InstallationCertificateSelection
{
    public IReadOnlyList<InstallationCertificateMaterialOption> Materials { get; }

    public InstallationCertificateSelection(IEnumerable<Item> materials)
    {
        Materials = materials
            .Where(material => !string.IsNullOrWhiteSpace(material.Name) && material.Quantity > 0)
            .OrderBy(material => material.SortOrder)
            .Select(material => new InstallationCertificateMaterialOption(material))
            .ToArray();
    }

    public void SelectAll(bool selected)
    {
        foreach (var option in Materials)
            option.IsSelected = selected;
    }

    public List<Item> GetSelectedMaterials()
    {
        var selected = Materials.Where(option => option.IsSelected).Select(option => option.Material).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("Seleziona almeno un materiale installato correttamente da inserire nel certificato.");

        return selected;
    }
}

public sealed class InstallationCertificateMaterialOption(Item material) : INotifyPropertyChanged
{
    public Item Material { get; } = material;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
