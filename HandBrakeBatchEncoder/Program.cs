using HandBrakeBatchEncoder;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace HandbrakeBatchEncoder
{
    /// <summary>
    /// FFmpeg NVENC batch encoder (formerly HandBrake batch encoder).
    /// Scans a root folder for video files and re-encodes them via ffmpeg.
    /// </summary>
    class Program
    {
        private static HBEState State = HBEState.Empty;

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== FFmpeg NVENC MKV Encoder ===\n");
            Console.Title = "== FFmpeg NVENC MKV Encoder ==";

            if (args.Length == 0 && !Debugger.IsAttached)
            {
                Console.WriteLine("Usage: FFmpegBatchEncoder <root-folder> <(Optional destination folder)>");
                return;
            }
            else if (Debugger.IsAttached)
            {
                args =
                [
                    "--RootFolder",       @"J:\jeff\files\3D\Movies",
                    "--DestFolder",       @"J:\jeff\files\Travel\Movies",
                    "--HoursThreshold",   "96",
                    // "--ForceReplace",
                    // "--ForceReEncode",
                    "--ConversionPreset", "xr",
                ];
            }

            string rootFolder = string.Empty;
            string destFolder = string.Empty;
            int hoursThreshold = 24;
            bool forceReplace = false;
            bool forceReEncode = false;
            int encodeMode = EncodingOptions.StandardPreset;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--RootFolder":
                        rootFolder = args[++i];
                        break;
                    case "--DestFolder":
                        destFolder = args[++i];
                        break;
                    case "--HoursThreshold":
                        hoursThreshold = int.Parse(args[++i]);
                        break;
                    case "--ForceReplace":
                        forceReplace = true;
                        break;
                    case "--ForceReEncode":
                        forceReEncode = true;
                        break;
                    case "--ConversionPreset":
                        encodeMode = EncodingOptions.ParsePreset(args[++i]);
                        break;
                    default:
                        Console.WriteLine($"Warning: Unrecognised argument - {args[i]}");
                        break;
                }
            }

            rootFolder ??= @"J:\jeff\files\3D\Movies";
            destFolder ??= @"J:\jeff\files\Travel\Movies";

            State = new HBEState(encodeMode)
            {
                RootFolder = rootFolder,
                DestinationFolder = destFolder,
                RecentHoursThreshold = hoursThreshold,
                ForceReplaceExisting = forceReplace,
                ForceReEncodeExisting = forceReEncode,
            };

            if (!Directory.Exists(rootFolder))
            {
                Console.WriteLine($"Error: Folder not found - {rootFolder}");
                return;
            }

            Console.WriteLine($"Processing root folder: {State.RootFolder}\n");
            Console.WriteLine($"Destination folder:     {State.DestinationFolder}\n");

            DateTime cutoff = DateTime.Now.AddHours(State.RecentHoursThreshold * -1);

            var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".mkv", ".mp4", ".m4v", ".avi", ".mpeg", ".mpg" };

            var videoFiles = Directory.EnumerateFiles(State.RootFolder, "*.*", System.IO.SearchOption.AllDirectories)
                .Where(f => !f.Contains(".deletedByTMM", StringComparison.OrdinalIgnoreCase))
                .Where(f => !f.Contains("-trailer.", StringComparison.OrdinalIgnoreCase))
                .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
                .Select(f => new FileInfo(f))
                .Where(f => f.LastAccessTime >= cutoff)
                .Select(fi => fi.FullName)
                .OrderBy(f => f)
                .ToList();

            if (videoFiles.Count == 0)
            {
                Console.WriteLine("No video files found.");
                return;
            }

            Console.WriteLine($"Got {videoFiles.Count} video file(s) ready to be processed...");
            int curFileCount = 1;
            HBEncoder myEncoder = new() { State = State };

            foreach (var file in videoFiles)
            {
                try
                {
                    // Set up destination paths
                    string outputDir = Path.Combine(destFolder, Path.GetFileName(Path.GetDirectoryName(file)!));
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string outputFile = Path.Combine(outputDir, $"{fileName}.mkv");

                    // Make sure that the Season folder (or other sub-folder) exists
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Debug
                    Debug.WriteLine("Output file path: " + outputFile);
                    Debug.WriteLine("Output directory: " + outputDir);
                    Debug.WriteLine("File name: " + fileName);
                    Debug.WriteLine("Original file path: " + file);

                    // Set up the current video file object
                    VideoFile curVideo = new(file)
                    {
                        State = State,
                        OutputFilePath = outputFile,
                    };

                    // Check if we've already processed this file before
                    if (curVideo.AlreadyProcessed)
                    {
                        // Update the user
                        Console.Write("It appears that this file has already been processed");

                        // Check to see if we are forcing a re-encode of already processed files
                        if (!State.ForceReEncodeExisting)
                        {
                            continue;
                        }
                    }

                    // Save a copy of the original title and episode info before we potentially modify it for encoding
                    var (inputTitle, inputEpisode) = (curVideo.Title.Title, curVideo.Title.Episode);
                    // Check to see if the output file already exists
                    bool alreadyExists = curVideo.OutputExists;

                    // If it does, and we're not forcing a replace, skip it
                    if (alreadyExists && !State.ForceReplaceExisting)
                    {
                        // Update the user and skip it
                        curFileCount++;
                        curFileCount++;
                        Console.WriteLine($"Skipping (already encoded match): {fileName} - " +
                            $"{curFileCount:#000}/{videoFiles.Count:#000}");
                        continue;
                    }
                    else if (alreadyExists && State.ForceReplaceExisting)
                    {
                        File.Delete(outputFile);    // bug fix: original deleted fileName, not outputFile
                    }

                    Console.WriteLine($"\nEncoding: {file} - {curFileCount:#000}/{videoFiles.Count:#000}");

                    if (myEncoder.EncodeVideo(curVideo))
                    {
                        Console.WriteLine($"\r\n✅ Finished: {outputFile}");
                        curFileCount++;
                    }
                    else
                    {
                        Console.WriteLine($"❌ Error encoding {outputFile}.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error encoding {file}: {ex.Message}");
                    continue;
                }
            }

            Console.WriteLine("\nAll done!");
        }
    }
}