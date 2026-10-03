using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
#pragma warning disable CA1001 // TreeView is not owned by this class, set to null in VPK_Disposed
    class PackageViewer(VrfGuiContext vrfGuiContext) : IViewer, IDisposable
#pragma warning restore CA1001
    {
#pragma warning disable CA2213 // TODO: Can we fix TreeView to be owned by this class?
        private TreeViewWithSearchResults? TreeView;
#pragma warning restore CA2213
        private VirtualPackageNode? VirtualRoot;
        private BetterTreeNode? LastContextTreeNode;
        private bool IsEditingPackage; // TODO: Allow editing existing vpks (but not chunked ones)

        public static bool IsAccepted(uint magic)
        {
            return magic == ValvePak.Package.MAGIC;
        }

        public Control CreateEmpty()
        {
            IsEditingPackage = true;

            var package = new Package();
            package.AddFile("README.txt", []); // TODO: Otherwise package.Entries is null

            vrfGuiContext.CurrentPackage = package;

            VirtualRoot = new VirtualPackageNode("root", 0, null);
            CreateTreeViewWithSearchResults();

            if (TreeView == null)
            {
                throw new InvalidOperationException("TreeView was not created");
            }

            return TreeView;
        }

        public async Task LoadAsync(Stream? stream)
        {
            var package = new Package();
            package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);

            if (stream != null)
            {
                package.SetFileName(vrfGuiContext.FileName);
                package.Read(stream);
            }
            else
            {
                package.Read(vrfGuiContext.FileName);
            }

            vrfGuiContext.CurrentPackage = package;

            VirtualRoot = new VirtualPackageNode("root", 0, null);

            if (vrfGuiContext.CurrentPackage.Entries != null)
            {
                foreach (var fileType in vrfGuiContext.CurrentPackage.Entries)
                {
                    foreach (var file in fileType.Value)
                    {
                        BetterTreeView.AddFileNode(VirtualRoot, file);
                    }
                }
            }

            foreach (var node in VirtualRoot.Folders)
            {
                VirtualRoot.TotalSize += node.Value.TotalSize;
            }

            foreach (var node in VirtualRoot.Files)
            {
                VirtualRoot.TotalSize += node.TotalLength;
            }
        }

        public void Create(TabPage tab)
        {
            CreateTreeViewWithSearchResults();
            tab.Controls.Add(TreeView);
        }

        private void CreateTreeViewWithSearchResults()
        {
            if (VirtualRoot == null)
            {
                throw new InvalidOperationException("VirtualRoot must be initialized before creating TreeView");
            }

            // create a TreeView with search capabilities, register its events, and add it to the tab
            TreeView = new TreeViewWithSearchResults(this);
            TreeView.InitializeTreeViewFromPackage(vrfGuiContext, VirtualRoot);
            TreeView.OpenPackageEntry += VPK_OpenFile;
            TreeView.OpenContextMenu += VPK_OnContextMenu;
            TreeView.PreviewFile += VPK_PreviewFile;
            TreeView.PreviewCleared += VPK_PreviewCleared;
            TreeView.PreviewFocused += VPK_PreviewFocused;
            TreeView.PreviewBlurred += VPK_PreviewBlurred;
            TreeView.Disposed += VPK_Disposed;
        }

        public void AddFolder(string directory)
        {
            var prefix = GetCurrentPrefix();

            if (prefix.Length > 0)
            {
                directory = Path.Join(prefix, directory);
            }

            directory = directory.Replace('\\', ValvePak.Package.DirectorySeparatorChar);

            TreeView?.AddFolderNode(directory);
        }

        public void AddFilesFromFolder(string inputDirectory)
        {
            var files = new FileSystemEnumerable<string>(
                inputDirectory,
                (ref entry) => entry.ToSpecifiedFullPath(),
                new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                }
            );

            AddFiles(files, inputDirectory);
        }

        public void AddFiles(IEnumerable<string> files, string? inputDirectory = null)
        {
            if (TreeView == null)
            {
                return;
            }

            var prefix = GetCurrentPrefix();

            Cursor.Current = Cursors.WaitCursor;

            TreeView.BeginUpdate();

            var resourceEntries = new Queue<(string PathOnDisk, PackageEntry Entry)>();

            // TODO: This is not adding to the selected folder, but to root
            foreach (var file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                var name = inputDirectory == null ? Path.GetFileName(file) : file[(inputDirectory.Length + 1)..];
                var data = File.ReadAllBytes(file);

                if (prefix.Length > 0)
                {
                    name = Path.Join(prefix, name);
                }

                if (vrfGuiContext.CurrentPackage == null)
                {
                    continue;
                }

                var entry = vrfGuiContext.CurrentPackage.AddFile(name, data);
                TreeView.AddFileNode(entry);

                if (data.Length >= 6)
                {
                    var magicResourceVersion = BitConverter.ToUInt16(data, 4);

                    if (Viewers.Resource.IsAccepted(magicResourceVersion) && name.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                    {
                        resourceEntries.Enqueue((file, entry));
                    }
                }
            }

            TreeView.EndUpdate();

            // Reveal the added entries under the folder the context menu was opened on
            LastContextTreeNode?.Expand();

            Cursor.Current = Cursors.Default;

#if DEBUG
            ScanForResourceDependencies();

            // Fire-and-forget on purpose: this is a dev-only prompt, and AddFiles has no reason to wait for it.
            async void ScanForResourceDependencies()
            {
                if (resourceEntries.Count == 0 || !await AppMessageDialogs.ConfirmAsync(
                    "Would you like to scan for all dependencies of the compiled file (ending in \"_c\") you just added?",
                    "Detected a compiled resource",
                    buttons: ConfirmButtons.YesNo).ConfigureAwait(true))
                {
                    return;
                }

                await AppMessageDialogs.ShowMessageAsync("This is not yet implemented.", "Not implemented").ConfigureAwait(true);

                while (resourceEntries.TryDequeue(out var entry))
                {
                    if (vrfGuiContext.CurrentPackage == null)
                    {
                        break;
                    }

                    vrfGuiContext.CurrentPackage.ReadEntry(entry.Entry, out var output, false);
                    using var entryStream = new MemoryStream(output);

                    using var resource = new ValveResourceFormat.Resource();
                    resource.Read(entryStream);

                    if (resource.ExternalReferences is null)
                    {
                        continue;
                    }

                    // TODO: This doesn't work properly
                    var folderDepth = entry.Entry.DirectoryName.Count(static c => c == Package.DirectorySeparatorChar);
                    var folder = Path.GetDirectoryName(entry.PathOnDisk.AsSpan());

                    while (folderDepth-- > 0)
                    {
                        folder = Path.GetDirectoryName(folder);
                    }

                    foreach (var reference in resource.ExternalReferences.ResourceRefInfoList)
                    {
                        if (reference.Name.StartsWith("_bakeresourcecache", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        // Do not recurse maps (skyboxes)
                        if (reference.Name.EndsWith(".vmap", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var file = Path.Combine(folder.ToString(), reference.Name);

                        if (!File.Exists(file))
                        {
                            Log.Warn(nameof(PackageViewer), $"Failed to find file: {file}");
                            continue;
                        }
                    }
                }
            }
#endif
        }

        public void RemoveCurrentFiles()
        {
            if (LastContextTreeNode != null)
            {
                var removedRoot = LastContextTreeNode.PkgNode;
                RemoveRecursiveFiles(LastContextTreeNode);

                if (removedRoot != null)
                {
                    TreeView?.PruneNavigationHistory(removedRoot);
                }
            }
        }

        public void RemoveRecursiveFiles(BetterTreeNode node)
        {
            if (node.PkgNode != null && node.Parent is BetterTreeNode parentNode)
            {
                parentNode.PkgNode?.Folders.Remove(node.PkgNode.Name);
            }

            for (var i = node.Nodes.Count - 1; i >= 0; i--)
            {
                RemoveRecursiveFiles((BetterTreeNode)node.Nodes[i]);
            }

            if (node.PackageEntry != null && node.Parent is BetterTreeNode parentNode2)
            {
                parentNode2.PkgNode?.Files.Remove(node.PackageEntry);
                vrfGuiContext.CurrentPackage?.RemoveFile(node.PackageEntry);
            }

            if (node.Level > 0)
            {
                node.Remove();
            }
        }

        public void SaveToFile(string fileName)
        {
            if (vrfGuiContext.CurrentPackage == null)
            {
                return;
            }

            vrfGuiContext.CurrentPackage.Write(fileName);

            var fileCount = 0;
            var fileSize = 0u;

            if (vrfGuiContext.CurrentPackage.Entries != null)
            {
                foreach (var fileType in vrfGuiContext.CurrentPackage.Entries)
                {
                    foreach (var file in fileType.Value)
                    {
                        fileCount++;
                        fileSize += file.TotalLength;
                    }
                }
            }

            var result = $"Created {Path.GetFileName(fileName)} with {fileCount} files of size {HumanReadableByteSizeFormatter.Format(fileSize)}.";

            Log.Info(nameof(PackageViewer), result);

            _ = AppMessageDialogs.ShowMessageAsync(result, "VPK created");
        }

        private void VPK_Disposed(object? sender, EventArgs e)
        {
            if (sender is TreeViewWithSearchResults treeViewWithSearch)
            {
                treeViewWithSearch.OpenPackageEntry -= VPK_OpenFile;
                treeViewWithSearch.OpenContextMenu -= VPK_OnContextMenu;
                treeViewWithSearch.PreviewFile -= VPK_PreviewFile;
                treeViewWithSearch.PreviewCleared -= VPK_PreviewCleared;
                treeViewWithSearch.PreviewFocused -= VPK_PreviewFocused;
                treeViewWithSearch.PreviewBlurred -= VPK_PreviewBlurred;
                treeViewWithSearch.Disposed -= VPK_Disposed;
                TreeView = null;
                LastContextTreeNode = null;
            }
        }

        /// <summary>
        /// Opens the given package entry in a new tab.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="entry">The package entry to open.</param>
        private void VPK_OpenFile(object? sender, PackageEntry entry)
        {
            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry);
        }

        private void VPK_PreviewCleared(object? sender, EventArgs e)
        {
            // A folder is shown instead of a file preview, so the window title should no longer reflect a file.
            Program.MainForm.ResetPreviewTitle();
        }

        private void VPK_PreviewFocused(object? sender, TabPage previewTab)
        {
            Program.MainForm.ShowPreviewKeybindings(previewTab);
        }

        private void VPK_PreviewBlurred(object? sender, EventArgs e)
        {
            Program.MainForm.ShowSelectedTabKeybindings();
        }

        private void VPK_PreviewFile(object? sender, PackageEntry entry)
        {
            if (TreeView == null)
            {
                return;
            }

            if (!TreeViewWithSearchResults.CanQuickPreviewFile(entry))
            {
                return;
            }

            var newVrfGuiContext = new VrfGuiContext(entry.GetFullPath(), vrfGuiContext);
            Program.MainForm.OpenFile(newVrfGuiContext, entry, TreeView);
        }

        private string GetCurrentPrefix()
        {
            var prefix = string.Empty;
            TreeNode? parent = LastContextTreeNode;

            while (parent != null && parent.Level > 0)
            {
                prefix = Path.Join(parent.Name, prefix);
                parent = parent.Parent;
            }

            return prefix;
        }

        /// <summary>
        /// Opens a context menu where the user right-clicked in the TreeView or ListView.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void VPK_OnContextMenu(object? sender, PackageContextMenuEventArgs e)
        {
            if (TreeView == null)
            {
                return;
            }

            var isRoot = e.PkgNode == TreeView.mainTreeView.Root;
            var isFolder = e.PackageEntry is null;

            if (e.TreeNode is not null)
            {
                isFolder = e.TreeNode.IsFolder;
            }

            if (IsEditingPackage)
            {
                if (e.TreeNode != null)
                {
                    LastContextTreeNode = e.TreeNode;

                    if (sender is Control control)
                    {
                        Program.MainForm.ShowVpkEditingContextMenu(control, e.Location, isRoot, isFolder);
                    }
                }

                return;
            }

            if (sender is Control senderControl)
            {
                Program.MainForm.ShowVpkContextMenu(senderControl, e.Location, isRoot, isFolder, TreeView.DeletedFilesRecovered);
            }
        }

        public void Dispose()
        {
            TreeView = null;
        }
    }
}
