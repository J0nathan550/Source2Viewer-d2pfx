namespace GUI.Types.PackageViewer.ThumbnailRenderers;

// Settings clamps the stored grid size against this, keep in step with the WinForms GUI's thumbnail renderer
internal enum ThumbnailSizes : int
{
    Tiny = 24,
    Small = 64,
    Medium = 128,
    Big = 192,
    Huge = 256,
}
