namespace GUI.Controls;

/// <summary>
/// Something that can show a file preview in place of opening a main window tab, such as the package browser.
/// </summary>
interface IPreviewHost
{
    /// <summary>Shows <paramref name="tab"/>, which owns the previewed viewer, and disposes the previous preview.</summary>
    void ShowPreview(DocumentTab tab);
}
