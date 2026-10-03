using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ValvePak;

namespace GUI.Types.PackageViewer
{
    /// <summary>
    /// Finds package entries whose data contains a byte pattern by streaming the archives,
    /// so memory use stays at one read buffer per archive instead of the archive size.
    /// </summary>
    static class PackageContentSearch
    {
        private const int ChunkSize = 4 * 1024 * 1024;
        private const ushort DirArchiveIndex = 0x7FFF;

        public static List<PackageEntry> Search(Package package, byte[] pattern, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            if (pattern.Length < 3)
            {
                throw new ArgumentException("Search input is too short.", nameof(pattern));
            }

            var results = new List<PackageEntry>();

            if (package.Entries == null)
            {
                return results;
            }

            var maxArchiveIndex = -1;
            var sortedEntriesPerArchive = new Dictionary<int, List<PackageEntry>>();

            foreach (var entry in package.Entries.Values.SelectMany(static entries => entries))
            {
                if (entry.ArchiveIndex != DirArchiveIndex && entry.ArchiveIndex > maxArchiveIndex)
                {
                    maxArchiveIndex = entry.ArchiveIndex;
                }

                if (entry.Length == 0)
                {
                    continue;
                }

                if (!sortedEntriesPerArchive.TryGetValue(entry.ArchiveIndex, out var archiveEntries))
                {
                    archiveEntries = [];
                    sortedEntriesPerArchive.Add(entry.ArchiveIndex, archiveEntries);
                }

                archiveEntries.Add(entry);
            }

            foreach (var archiveEntries in sortedEntriesPerArchive.Values)
            {
                archiveEntries.Sort(static (a, b) => a.Offset.CompareTo(b.Offset));
            }

            if (sortedEntriesPerArchive.TryGetValue(DirArchiveIndex, out var sortedEntriesInDirVpk))
            {
                var fileName = $"{package.FileName}{(package.IsDirVPK ? "_dir" : "")}.vpk";

                progress?.Report($"Searching '{fileName}'");
                results.AddRange(SearchFile(fileName, pattern, sortedEntriesInDirVpk, cancellationToken));
            }

            if (maxArchiveIndex > -1)
            {
                var matches = new HashSet<PackageEntry>();
                var archivesScanned = 0;

                Parallel.For(0, maxArchiveIndex + 1, new ParallelOptions
                {
                    MaxDegreeOfParallelism = 3,
                    CancellationToken = cancellationToken,
                }, archiveIndex =>
                {
                    if (!sortedEntriesPerArchive.TryGetValue(archiveIndex, out var archiveEntries))
                    {
                        return;
                    }

                    var fileName = $"{package.FileName}_{archiveIndex:D3}.vpk";
                    var archiveMatches = SearchFile(fileName, pattern, archiveEntries, cancellationToken);

                    lock (matches)
                    {
                        foreach (var match in archiveMatches)
                        {
                            if (matches.Add(match))
                            {
                                results.Add(match);
                            }
                        }
                    }

                    var scanned = Interlocked.Increment(ref archivesScanned);
                    progress?.Report($"Searched {scanned} vpks out of {maxArchiveIndex + 1}, found {matches.Count} matches so far");
                });
            }

            progress?.Report($"Found {results.Count} matches");

            return results;
        }

        /// <summary>
        /// Searches one archive file. <paramref name="sortedEntries"/> must be sorted by offset.
        /// </summary>
        public static HashSet<PackageEntry> SearchFile(string fileName, byte[] pattern, List<PackageEntry> sortedEntries, CancellationToken cancellationToken)
        {
            var matches = new HashSet<PackageEntry>();

            // The tail of each chunk is carried over so matches that straddle two reads are found
            var overlap = pattern.Length - 1;
            var buffer = ArrayPool<byte>.Shared.Rent(ChunkSize + overlap);

            try
            {
                using var stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1, FileOptions.SequentialScan);

                long bufferFileOffset = 0;
                var carried = 0;
                var entryIndex = 0;
                int read;

                while ((read = stream.Read(buffer, carried, ChunkSize)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var data = buffer.AsSpan(0, carried + read);
                    var searchFrom = 0;

                    while (true)
                    {
                        var index = data[searchFrom..].IndexOf(pattern);

                        if (index < 0)
                        {
                            break;
                        }

                        var matchOffset = bufferFileOffset + searchFrom + index;
                        searchFrom += index + 1;

                        while (entryIndex < sortedEntries.Count && sortedEntries[entryIndex].Offset + (long)sortedEntries[entryIndex].Length <= matchOffset)
                        {
                            entryIndex++;
                        }

                        if (entryIndex >= sortedEntries.Count)
                        {
                            return matches;
                        }

                        if (sortedEntries[entryIndex].Offset <= matchOffset)
                        {
                            matches.Add(sortedEntries[entryIndex]);
                        }
                    }

                    carried = Math.Min(overlap, data.Length);
                    data[^carried..].CopyTo(buffer);
                    bufferFileOffset += data.Length - carried;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return matches;
        }
    }
}
