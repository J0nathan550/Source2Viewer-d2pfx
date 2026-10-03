using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace GUI.Forms
{
    /// <summary>
    /// The package Find dialog, like the WinForms SearchForm. Kept per main window so the last search is remembered.
    /// </summary>
    sealed class SearchWindow : Window
    {
        internal static readonly StringComparer NumericComparer =
            StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        private static readonly (string Name, SearchType Type)[] SearchTypes =
        [
            ("File Name (Partial Match)", SearchType.FileNamePartialMatch),
            ("File Name (Exact Match)", SearchType.FileNameExactMatch),
            ("File Full Path", SearchType.FullPath),
            ("Regex", SearchType.Regex),
            ("File Contents (Case Sensitive)", SearchType.FileContents),
            ("File Contents Hex Bytes", SearchType.FileContentsHex),
        ];

        private readonly TextBox findTextBox;
        private readonly ComboBox searchTypeComboBox;
        private readonly ComboBox filterKeyComboBox;
        private readonly ComboBox filterValueComboBox;
        private Dictionary<string, SortedSet<string>>? filterKeys;
        private int filterLoadVersion;

        public string SearchText => findTextBox.Text ?? string.Empty;

        public SearchType SelectedSearchType => SearchTypes[Math.Max(0, searchTypeComboBox.SelectedIndex)].Type;

        public string? SelectedFilterKey => filterKeys != null && filterKeyComboBox.SelectedIndex > 0 ? filterKeyComboBox.SelectedItem as string : null;

        public string? SelectedFilterValue => SelectedFilterKey != null && filterValueComboBox.SelectedIndex > 0 ? filterValueComboBox.SelectedItem as string : null;

        public SearchWindow()
        {
            Title = "Find";
            Width = 432;
            SizeToContent = SizeToContent.Height;
            CanResize = false;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            findTextBox = new TextBox();
            findTextBox.Classes.Add("pathBox");

            searchTypeComboBox = new ComboBox
            {
                ItemsSource = SearchTypes.Select(static t => t.Name).ToArray(),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };

            filterKeyComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
            filterKeyComboBox.SelectionChanged += (_, _) => OnFilterKeyChanged();

            filterValueComboBox = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsVisible = false };

            var findButton = new Button { Content = "Find", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, IsDefault = true };
            findButton.Click += (_, _) => Close(true);

            var cancelButton = new Button { Content = "Cancel", MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center, IsCancel = true };
            cancelButton.Click += (_, _) => Close(false);

            Content = new StackPanel
            {
                Margin = new(12),
                Spacing = 8,
                Children =
                {
                    findTextBox,
                    searchTypeComboBox,
                    new TextBlock { Text = "Asset info filter:" },
                    filterKeyComboBox,
                    filterValueComboBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Margin = new(0, 8, 0, 0),
                        Children = { findButton, cancelButton },
                    },
                },
            };

            Opened += (_, _) =>
            {
                findTextBox.Focus();
                findTextBox.SelectAll();
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Close(false);
                }
            };
        }

        /// <summary>
        /// Sets the available SearchableUserData filter keys, showing a loading state until the task completes.
        /// </summary>
        public void SetSearchableUserDataKeys(Task<Dictionary<string, SortedSet<string>>?> keysTask)
        {
            if (keysTask.IsCompletedSuccessfully && ReferenceEquals(keysTask.Result, filterKeys) && filterKeys != null)
            {
                return;
            }

            var version = ++filterLoadVersion;

            if (keysTask.IsCompleted)
            {
                PopulateFilterKeys(keysTask.IsCompletedSuccessfully ? keysTask.Result : null);
                return;
            }

            filterKeys = null;
            filterKeyComboBox.ItemsSource = new[] { "Loading asset info\u2026" };
            filterKeyComboBox.SelectedIndex = 0;
            filterKeyComboBox.IsEnabled = false;
            filterValueComboBox.IsVisible = false;

            keysTask.ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (filterLoadVersion == version)
                {
                    PopulateFilterKeys(t.IsCompletedSuccessfully ? t.Result : null);
                }
            }), TaskScheduler.Default);
        }

        private void PopulateFilterKeys(Dictionary<string, SortedSet<string>>? keys)
        {
            filterKeys = keys is { Count: > 0 } ? keys : null;

            if (filterKeys != null)
            {
                filterKeyComboBox.ItemsSource = filterKeys.Keys.Order(NumericComparer).Prepend("(No filter)").ToArray();
                filterKeyComboBox.IsEnabled = true;
            }
            else
            {
                filterKeyComboBox.ItemsSource = new[] { "No asset info" };
                filterKeyComboBox.IsEnabled = false;
            }

            filterKeyComboBox.SelectedIndex = 0;
            filterValueComboBox.IsVisible = false;
        }

        private void OnFilterKeyChanged()
        {
            if (SelectedFilterKey is not { } key || filterKeys == null || !filterKeys.TryGetValue(key, out var values))
            {
                filterValueComboBox.IsVisible = false;
                return;
            }

            filterValueComboBox.ItemsSource = values.Prepend("(Any value)").ToArray();
            filterValueComboBox.SelectedIndex = 0;
            filterValueComboBox.IsVisible = true;
        }
    }
}
