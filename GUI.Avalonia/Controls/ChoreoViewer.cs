using System.Globalization;
using System.IO;
using System.Text;
using Avalonia.Controls;
using AvaloniaEdit;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Controls
{
    /// <summary>The scenes of a vcdlist, with the selected scene's events as KV3.</summary>
    sealed class ChoreoViewer : Grid
    {
        public sealed record SceneRow(string Name, string Version, string Duration, string SoundDuration, string HasSounds)
        {
            internal int? Index { get; init; }
        }

        private readonly ChoreoSceneFileData choreoDataList;
        private readonly TextEditor textBox;

        public ChoreoViewer(Resource resource)
        {
            var dataBlock = (ChoreoSceneFileData?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(dataBlock);
            choreoDataList = dataBlock;

            var fileName = Path.GetFileNameWithoutExtension(resource.FileName) + ".vcdlist";

            var rows = new List<SceneRow>
            {
                new(fileName, choreoDataList.Version.ToString(CultureInfo.InvariantCulture), string.Empty, string.Empty, string.Empty),
            };

            for (var i = 0; i < choreoDataList.Scenes.Length; i++)
            {
                var scene = choreoDataList.Scenes[i];
                rows.Add(new SceneRow(
                    scene.Name ?? string.Empty,
                    scene.Version.ToString(CultureInfo.InvariantCulture),
                    FormatMilliseconds(scene.Duration),
                    FormatMilliseconds(scene.SoundDuration),
                    scene.HasSounds ? "Yes" : "No")
                {
                    Index = i,
                });
            }

            var fileListView = ViewerContentPresenter.CreateGrid(rows);
            textBox = CodeTextBox.Create(string.Empty, HighlightLanguage.KeyValues);

            fileListView.SelectionChanged += (_, _) =>
            {
                if (fileListView.SelectedItem is not SceneRow row)
                {
                    textBox.Text = string.Empty;
                    return;
                }

                if (row.Index is { } index)
                {
                    textBox.Text = choreoDataList.Scenes[index].ToKeyValues().ToKV3String();
                }
                else
                {
                    ShowVcdList();
                }
            };

            RowDefinitions = new RowDefinitions("2*,4,3*");
            var splitter = new GridSplitter { ResizeDirection = GridResizeDirection.Rows };
            SetRow(splitter, 1);
            SetRow(textBox, 2);
            Children.Add(fileListView);
            Children.Add(splitter);
            Children.Add(textBox);
        }

        private static string FormatMilliseconds(int milliseconds)
        {
            return (milliseconds / 1000f).ToString("0.000", CultureInfo.InvariantCulture);
        }

        private void ShowVcdList()
        {
            var sb = new StringBuilder();
            foreach (var scene in choreoDataList.Scenes)
            {
                sb.AppendLine(scene.Name);
            }
            textBox.Text = sb.ToString();
        }
    }
}
