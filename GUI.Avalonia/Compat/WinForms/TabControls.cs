// See Keys.cs for why this namespace exists in an Avalonia project.

namespace System.Windows.Forms;

/// <summary>
/// A tab, which shared viewers find through their sidebar's parent to bring themselves to the front.
/// It is a real Avalonia tab, so it can be used anywhere one is expected.
/// </summary>
public class TabPage : Avalonia.Controls.TabItem
{
    public TabPage()
    {
    }

    public TabPage(string text)
    {
        Header = text;
    }

    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.TabItem);
}

/// <inheritdoc cref="TabPage"/>
public class TabControl : Avalonia.Controls.TabControl
{
    protected override Type StyleKeyOverride => typeof(Avalonia.Controls.TabControl);

    public void SelectTab(TabPage page) => SelectedItem = page;
}
