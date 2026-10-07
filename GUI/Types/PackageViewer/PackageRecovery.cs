using System.Buffers;
using System.IO;
using System.Linq;
using System.Text;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using ValveResourceFormat.IO;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Progress of a long operation shown as a status text and a bar.
    /// </summary>
    interface IProgressBarReporter
    {
        void SetProgress(string text);
        void SetBarMax(int count);
        void SetBarValue(int value);
    }

    /// <summary>
    /// Finds resources in the unused gaps of package archives, which are often files deleted in an update.
    /// </summary>
    static class PackageRecovery
    {
        public static List<PackageEntry> RecoverDeletedFiles(Package package, IProgressBarReporter progress)
        {
            if (package.Entries == null)
            {
                return [];
            }

            var allEntries = package.Entries
                .SelectMany(file => file.Value)
                .OrderBy(file => file.Offset)
                .GroupBy(file => file.ArchiveIndex)
                .OrderBy(x => x.Key)
                .ToDictionary(x => x.Key, x => x.ToList());

            // Every known entry has a gap in front of it to scan, so that is the unit of progress
            progress.SetBarMax(allEntries.Sum(x => x.Value.Count));
            var processed = 0;

            var hiddenIndex = 0;
            var totalSlackSize = 0u;
            var hiddenFiles = new List<PackageEntry>();
            var kv3header = Encoding.ASCII.GetBytes("<!-- kv3 ");
            var previousArchiveIndex = 0;

            foreach (var (archiveIndex, entries) in allEntries)
            {
                if (archiveIndex - previousArchiveIndex > 1)
                {
                    Log.Warn(nameof(PackageRecovery), $"There is probably an unused {previousArchiveIndex:D3}.vpk");
                }

                previousArchiveIndex = archiveIndex;

                var nextOffset = 0u;

                void FindValidFiles(uint entryOffset, uint entryLength)
                {
                    var offset = nextOffset;
                    nextOffset = entryOffset + entryLength;

                    totalSlackSize += entryOffset - offset;

                    var scan = true;

                    while (scan)
                    {
                        scan = false;

                        if (offset == entryOffset)
                        {
                            break;
                        }

                        offset = offset + 16 - 1 & ~(16u - 1); // TODO: Validate this gap

                        var length = entryOffset - offset;

                        if (length <= 16)
                        {
                            // TODO: Verify what this gap is, seems to be null bytes
                            break;
                        }

                        hiddenIndex++;
                        var newEntry = new PackageEntry
                        {
                            FileName = $"Archive {archiveIndex:D3} File {hiddenIndex}",
                            DirectoryName = "Undetected filenames",
                            TypeName = " ",
                            CRC32 = 0,
                            SmallData = [],
                            ArchiveIndex = archiveIndex,
                            Offset = offset,
                            Length = length,
                        };

                        var bytes = ArrayPool<byte>.Shared.Rent((int)newEntry.TotalLength);

                        try
                        {
                            package.ReadEntry(newEntry, bytes, validateCrc: false);
                            using var stream = new MemoryStream(bytes, 0, (int)newEntry.TotalLength);
                            using var resource = new ValveResourceFormat.Resource();
                            resource.Read(stream);

                            var fileSize = resource.FullFileSize;

                            if (fileSize != length)
                            {
                                if (fileSize > length)
                                {
                                    throw new InvalidDataException("Resource filesize is bigger than the gap length we found");
                                }

                                newEntry.Length = fileSize;
                                offset += fileSize;
                                scan = true;
                            }

                            string? resourceTypeExtensionWithDot = null;

                            if (resource.ResourceType != ResourceType.Unknown)
                            {
                                var resourceTypeExtension = resource.ResourceType.GetExtension();
                                resourceTypeExtensionWithDot = string.Concat(".", resourceTypeExtension);
                                newEntry.TypeName = string.Concat(resourceTypeExtension, GameFileLoader.CompiledFileSuffix);
                            }

                            string? filepath = null;

                            // Use input dependency as the file name if there is one
                            if (resource.EditInfo != null)
                            {
                                filepath = RecoverDeletedFilesGetPossiblePath(resource.EditInfo.InputDependencies, resourceTypeExtensionWithDot);
                                filepath ??= RecoverDeletedFilesGetPossiblePath(resource.EditInfo.AdditionalInputDependencies, resourceTypeExtensionWithDot);

                                // Fix panorama extension
                                if (filepath != null && resourceTypeExtensionWithDot == ".vtxt")
                                {
                                    newEntry.TypeName = string.Concat(Path.GetExtension(filepath)[1..], GameFileLoader.CompiledFileSuffix);
                                }
                            }

                            if (filepath != null)
                            {
                                var dirName = Path.GetDirectoryName(filepath);
                                if (dirName != null)
                                {
                                    newEntry.DirectoryName = dirName.Replace('\\', ValvePak.Package.DirectorySeparatorChar);
                                }
                                newEntry.FileName = Path.GetFileNameWithoutExtension(filepath);
                            }
                            else
                            {
                                newEntry.DirectoryName += string.Concat(ValvePak.Package.DirectorySeparatorChar, resource.ResourceType);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Debug(nameof(PackageRecovery), $"File {hiddenIndex} - {ex.Message}");

                            newEntry.FileName += $" ({length} bytes)";

                            var span = bytes.AsSpan(0, (int)newEntry.TotalLength);

                            if (span.StartsWith(kv3header))
                            {
                                newEntry.TypeName = "kv3";
                            }
                            else if (!span.Contains((byte)0))
                            {
                                newEntry.TypeName = "txt";
                            }
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(bytes);
                        }

                        if (!package.Entries.TryGetValue(newEntry.TypeName, out var typeEntries))
                        {
                            typeEntries = [];
                            package.Entries.Add(newEntry.TypeName, typeEntries);
                        }

                        typeEntries.Add(newEntry);
                        hiddenFiles.Add(newEntry);

                        progress.SetProgress($"Found {hiddenFiles.Count} files ({HumanReadableByteSizeFormatter.Format(totalSlackSize)}) so far…");
                    }
                }

                // Recover files in gaps between entries
                foreach (var entry in entries)
                {
                    progress.SetBarValue(++processed);

                    if (entry.Length == 0)
                    {
                        continue;
                    }

                    FindValidFiles(entry.Offset, entry.Length);
                }

                // Recover files in archives after last possible entry for that archive
                if (archiveIndex != short.MaxValue)
                {
                    var archiveFileSize = nextOffset;

                    try
                    {
                        archiveFileSize = (uint)new FileInfo($"{package.FileName}_{archiveIndex:D3}.vpk").Length;
                    }
                    catch (Exception e)
                    {
                        Log.Debug(nameof(PackageRecovery), e.Message);
                    }

                    if (archiveFileSize != nextOffset)
                    {
                        FindValidFiles(archiveFileSize, 0);
                    }
                }
            }

            Log.Info(nameof(PackageRecovery), $"Found {hiddenIndex} deleted files totaling {HumanReadableByteSizeFormatter.Format(totalSlackSize)}");

            return hiddenFiles;
        }

        private static string? RecoverDeletedFilesGetPossiblePath(List<InputDependency> inputDeps, string? resourceTypeExtensionWithDot)
        {
            if (inputDeps.Count == 0)
            {
                return null;
            }

            foreach (var inputDependency in inputDeps)
            {
                if (Path.GetExtension(inputDependency.ContentRelativeFilename) == resourceTypeExtensionWithDot)
                {
                    return inputDependency.ContentRelativeFilename;
                }
            }

            // We can't detect correct panorama file type from compiler information, so we have to guess
            if (resourceTypeExtensionWithDot == ".vtxt")
            {
                var preferredExtensions = new string[]
                {
                    ".vcss",
                    ".vxml",
                    ".vpdi",
                    ".vjs",
                    ".vts",
                };

                foreach (var inputDependency in inputDeps)
                {
                    if (preferredExtensions.Contains(Path.GetExtension(inputDependency.ContentRelativeFilename)))
                    {
                        return inputDependency.ContentRelativeFilename;
                    }
                }
            }

            return inputDeps[0].ContentRelativeFilename;
        }
    }
}
