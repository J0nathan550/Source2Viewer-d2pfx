using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Controls;

/// <summary>
/// Lists every block of a type that a resource repeats, such as the per mesh blocks of a model,
/// and shows the selected one, instead of a tab per block.
/// </summary>
sealed class RepeatedBlocksViewer : Grid
{
    /// <summary>A row of the block list, its properties are the list's columns.</summary>
    public sealed record BlockRow(string Name, int Block, string Size);

    private readonly Dictionary<BlockRow, Block> blocksByRow = [];
    private readonly Func<Block, Control> createBlockView;
    private readonly ContentControl blockView = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch,
    };

    public RepeatedBlocksViewer(List<(int Index, Block Block)> blocks, Dictionary<int, string> blockNames, Func<Block, Control> createBlockView)
    {
        this.createBlockView = createBlockView;

        var rows = blocks.Select(entry =>
        {
            var row = new BlockRow(
                blockNames.GetValueOrDefault(entry.Index, string.Empty),
                entry.Index,
                HumanReadableByteSizeFormatter.Format(entry.Block.Size));
            blocksByRow.Add(row, entry.Block);
            return row;
        }).ToList();

        var list = ViewerContentPresenter.CreateGrid(rows);
        list.SelectionMode = DataGridSelectionMode.Single;
        list.SelectionChanged += (_, _) => ShowBlock(list.SelectedItem as BlockRow);

        RowDefinitions = new RowDefinitions("200,Auto,*");

        SetRow(list, 0);
        Children.Add(list);

        var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows, Height = 4 };
        SetRow(splitter, 1);
        Children.Add(splitter);

        SetRow(blockView, 2);
        Children.Add(blockView);

        if (rows.Count > 0)
        {
            list.SelectedItem = rows[0];
        }
    }

    private void ShowBlock(BlockRow? row)
    {
        if (row == null || !blocksByRow.TryGetValue(row, out var block))
        {
            return;
        }

        try
        {
            blockView.Content = createBlockView(block);
        }
        catch (Exception e)
        {
            Log.Error(nameof(RepeatedBlocksViewer), e.ToString());
            blockView.Content = CodeTextBox.CreateFromException(e);
        }
    }
}
