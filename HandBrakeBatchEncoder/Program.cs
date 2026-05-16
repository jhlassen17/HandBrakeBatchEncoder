using HandBrakeBatchEncoder;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace HandbrakeBatchEncoder
{
    /// <summary>
    /// Handbrake batch encoder for XR glasses
    /// </summary>
    class Program
    {
        /// <summary>
        /// App state
        /// </summary>
        private static HBEState State = HBEState.Empty;

        // Main entry point
        static void Main(string[] args)
        {
            // Info
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("=== HandBrake NVENC MKV Encoder ===\n");
            Console.Title = "== HandBrake NVENC MKV Encoder ==";

            // Determine what commands were provided
            if (args.Length == 0 && !Debugger.IsAttached)
            {
                // They didn't provide anything, let them know
                Console.WriteLine("Usage: BatchHandbrakeEncoder <root-folder> <(Optional destination folder)>");
                return;
            }
            else if (Debugger.IsAttached)
            {
                // Debug args
                args =
                [
                    "--RootFolder", @"J:\jeff\files\3D\Movies",
                    "--DestFolder", @"J:\jeff\files\Travel\Movies",
                    "--HoursThreshold", "24",
                    // "--ForceReplace",
                    // "--ForceReEncode",
                    "--ConversionPreset", "xr",
                ];
            }

            // Set up folder paths
            string rootFolder = string.Empty;
            string destFolder = string.Empty;
            int hoursThreshold = 24;
            bool forceReplace = false;
            bool forceReEncode = false;
            int encodeMode = EncodingOptions.StandardPreset;

            // CLI Args
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
                        string tmpStr = args[++i];
                        encodeMode = EncodingOptions.ParsePreset(tmpStr);
                        break;
                }
            }

            rootFolder ??= @"J:\jeff\files\Video\TV";
            destFolder ??= @"J:\jeff\files\Temp\TV";

            // Set up state
            State = new HBEState(encodeMode)
            {
                RootFolder = rootFolder, 
                DestinationFolder = destFolder, 
                RecentHoursThreshold = hoursThreshold,
                ForceReplaceExisting = forceReplace,
                ForceReEncodeExisting = forceReEncode,
            };

            // Make sure that the provided root folder actually exists
            if (!Directory.Exists(rootFolder))
            {
                Console.WriteLine($"Error: Folder not found - {rootFolder}");
                return;
            }

            // Info
            Console.WriteLine($"Processing root folder: {State.RootFolder}\n");
            Console.WriteLine($"Setting destination folder: {State.DestinationFolder}\n");

            // Compute today's 12:07 AM
            DateTime cutoff = DateTime.Now.AddHours(State.RecentHoursThreshold * -1);


            var videoFiles = Directory
                .EnumerateFiles(State.RootFolder, "*.*", SearchOption.AllDirectories)
                .Where(fl => !fl.Contains(".deletedByTMM", StringComparison.OrdinalIgnoreCase))
                .Where(fi => !fi.Contains("-trailer.", StringComparison.OrdinalIgnoreCase))
                .Where(f => f.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".m4v", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".avi", StringComparison.OrdinalIgnoreCase))
                .Select(f => new FileInfo(f))
                .Where(f => f.LastAccessTime >= cutoff)
                .Select(fi => fi.FullName)
                .ToList();

            // Make sure that we got some files back
            if (videoFiles.Count == 0)

            {
                Console.WriteLine("No video files found.");
                return;
            }

            Console.WriteLine($"Got {videoFiles.Count} video file(s) ready to be processed...");
            int curFileCount = 1;
            HBEncoder myEncoder = new() { State = State };

            // Loop through each file
            foreach (var file in videoFiles)
            {
                try
                {
                    // Set up destination paths
                    string outputDir = Path.Combine(destFolder, Path.GetFileName(Path.GetDirectoryName(file)!)); // File won't be null here
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    string outputFile = Path.Combine(outputDir, $"{fileName}.mkv");

                    // Make sure that the Season folder (or other sub-folder) exists
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    Debug.WriteLine("Output file path: " + outputFile);
                    Debug.WriteLine("Output directory: " + outputDir);
                    Debug.WriteLine("File name: " + fileName);
                    Debug.WriteLine("Original file path: " + file);

                    // 
                    VideoFile curVideo = new(file, State)
                    {
                        State = State, 
                        OutputFilePath = outputFile,
                    };

                    if (curVideo.AlreadyProcessed)
                    {
                        Console.Write("It appears that this file has already been processed");
                        if (!State.ForceReEncodeExisting)
                        {
                            continue;
                        }
                    }

                    var (inputTitle, inputEpisode) = (curVideo.Title.Title, curVideo.Title.Episode);

                    bool alreadyExists = curVideo.OutputExists;

                    if (alreadyExists && !State.ForceReplaceExisting)
                    {
                        curFileCount++;
                        Console.WriteLine($"Skipping (already encoded match): {fileName} - " +
                            $"{curFileCount:#000}/{videoFiles.Count:#000}");
                        continue;
                    }
                    else if (alreadyExists && State.ForceReplaceExisting)
                    {
                        File.Delete(fileName);
                    }

                    // Info
                    Console.WriteLine($"\nEncoding: {file} - {curFileCount:#000}/" +
                            $"{videoFiles.Count:#000}");

                    // Encode the video
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
                    // Error
                    Console.WriteLine($"❌ Error encoding {file}: {ex.Message}");
                    continue;
                }
            }

            // Complete
            Console.WriteLine("\nAll done!");
        }
    }
}