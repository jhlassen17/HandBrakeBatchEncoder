using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HandBrakeBatchEncoder
{

    /// <summary>
    /// Represents the configuration and state information used for video encoding operations, including paths to
    /// required tools, processing options, and folder locations.
    /// </summary>
    public partial class HBEState
    {
        #region Instance Vars

        // Path to ffmpeg for encoding video files
        protected const string ffmpegPath = @"""C:\ProgramData\ChannelsDVR\latest\ffmpeg.exe""";
        // Path to ffprobe for analyzing video files (HDR detection, subtitle inspection, processed-tag check)
        protected const string ffprobePath = @"C:\ProgramData\Sonarr\bin\ffprobe.exe";
        // Path to mkvpropedit for setting MKV tags (marks processed files to avoid re-encoding)
        protected const string mkvpropeditPath = @"C:\Program Files\MKVToolNix\mkvpropedit.exe";

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor
        /// </summary>
        protected HBEState()
        {
            //
        }

        /// <summary>
        /// Initializes a new instance of the HBEState class with the specified encoding mode.
        /// </summary>
        /// <param name="encodeMode">An integer value specifying the encoding mode to use.</param>
        public HBEState(int encodeMode) : this()
        {
            this.EncodeMode = encodeMode;
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the full file system path to the ffmpeg executable.
        /// </summary>
        public static string FFmpegPath => ffmpegPath;

        /// <summary>
        /// Gets the full file system path to the ffprobe executable.
        /// </summary>
        public static string FFProbePath => ffprobePath;

        /// <summary>
        /// Gets the full file system path to the mkvpropedit executable.
        /// </summary>
        public static string MKVPropEditPath => mkvpropeditPath;

        /// <summary>
        /// Gets the processed tag name associated with this instance.
        /// </summary>
        public static string ProcessedTagName => "COPYRIGHT";

        /// <summary>
        /// Gets the processed tag value.
        /// </summary>
        public static string ProcessedTagValue => "processed";

        /// <summary>
        /// Gets the absolute path of the root folder used by the application.
        /// </summary>
        public required string RootFolder { get; init; }

        /// <summary>
        /// Gets or sets the path to the destination folder where output files are saved.
        /// </summary>
        public required string DestinationFolder { get; init; }

        /// <summary>
        /// Gets or sets the number of hours used to determine whether an item is considered recent.
        /// </summary>
        public int RecentHoursThreshold { get; set; } = 24;

        /// <summary>
        /// Gets or sets a value indicating whether existing items should be forcibly replaced.
        /// </summary>
        /// <remarks>Set this property to <see langword="true"/> to overwrite existing items without
        /// prompting or checking for conflicts. Use with caution, as enabling this option may result in loss of
        /// existing data.</remarks>
        public bool ForceReplaceExisting { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether existing files should be re-encoded even if they already exist.
        /// </summary>
        /// <remarks>Set this property to <see langword="true"/> to force the re-encoding of files that
        /// are already present. This can be useful when changes to encoding settings require all files to be updated,
        /// regardless of their current state.</remarks>
        public bool ForceReEncodeExisting { get; set; } = false;

        /// <summary>
        /// Gets the encoding mode preset used for encoding operations.
        /// </summary>
        /// <remarks>The encoding mode determines which preset configuration is applied when encoding
        /// data. The default value is <see cref="EncodingOptions.StandardPreset"/>. Derived classes can set this
        /// property to customize encoding behavior.</remarks>
        public int EncodeMode { get; protected set; } = EncodingOptions.StandardPreset;

        /// <summary>
        /// "Empty" state instance with default values, useful for testing or as a baseline configuration.
        /// </summary>
        public static HBEState Empty => new(EncodingOptions.StandardPreset)
        {
            RootFolder = string.Empty,
            DestinationFolder = string.Empty,
            RecentHoursThreshold = 24,
            ForceReplaceExisting = false,
            ForceReEncodeExisting = false,
        };

        #endregion

        #region Public Methods

        /// <summary>
        /// Parses and normalizes the file path to extract title and episode information.
        /// </summary>
        /// <remarks>Recognizes episode patterns (S01E01, S01E01E02) and title-year formats. Removes
        /// content within curly braces and square brackets before processing.</remarks>
        public static TitleDescriptor NormalizeTitle(VideoFile videoFile)
        {
            // Initialize
            string name = videoFile.FilePath;

            // Remove extension
            name = Path.GetFileNameWithoutExtension(name);

            // Remove {metadata} and [tags]
            name = MetadataRegex().Replace(name, "");
            name = SquareBracketRegex().Replace(name, "");

            // Normalize whitespace early
            name = WhitespaceRegex().Replace(name, " ").Trim();

            // ✅ Extract episode pattern (S01E01, S01E01E02, etc.)
            var epMatch = SeasonEpisodeRegex().Match(name);
            string episode = epMatch?.Success == true ? epMatch.Value.ToUpper() : null ?? string.Empty;

            // ✅ Extract "Title (Year)"
            var titleMatch = TitleYearRegex().Match(name);
            string title;

            // If we found a title in the "Title (Year)" format, use it. Otherwise, fallback to a
            // more aggressive cleanup that removes episode patterns and trailing metadata.
            if (titleMatch.Success)
            {
                title = titleMatch.Groups[1].Value.Trim();
            }
            else
            {
                // Fallback cleanup
                title = TrailingEpisodeRegex().Replace(name, "");
                title = TrailingMetadataRegex().Replace(title, "").Trim();
            }


            // Save normalized values
            TitleDescriptor tmpDesc = new(title, episode);
            return tmpDesc;
        }

        /// <summary>
        /// Utility method to run an external process with the specified executable and arguments, 
        /// capturing and optionally printing the output.
        /// </summary>
        /// <param name="exe">The path to the executable to run.</param>
        /// <param name="args">The arguments to pass to the executable.</param>
        /// <param name="printOutput">Whether to print the output to the debug console.</param>
        /// <returns>The combined standard output and error output from the process.</returns>
        public static string RunProcess(string exe, string args, bool printOutput = false)
        {
            // Set up process start info
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Start the process and capture output
            using var process = new Process { StartInfo = psi };

            // Use StringBuilder to efficiently capture output and error streams
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            // Set up event handlers to capture output and error data
            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            // Capture error output
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            // Start the process
            process.Start();

            // Begin asynchronous reading of output and error streams
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait for the process to exit
            process.WaitForExit();

            // Combine output and error into a single string
            var output = outputBuilder.ToString() + errorBuilder.ToString();

            // Optionally print the output to the debug console
            if (printOutput)
                Debug.WriteLine(output.ToString());

            // Return the combined output
            return output.ToString();
        }

        /// <summary>
        /// Gets a compiled regular expression that matches text enclosed in curly braces.
        /// </summary>
        /// <returns>A compiled regular expression instance.</returns>
        [GeneratedRegex(@"\{.*?\}")]
        private static partial Regex MetadataRegex();

        /// <summary>
        /// Gets a regular expression that matches text enclosed in square brackets.
        /// </summary>
        /// <returns>A compiled regular expression instance.</returns>
        [GeneratedRegex(@"\[.*?\]")]
        private static partial Regex SquareBracketRegex();

        /// <summary>
        /// Gets a regular expression that matches whitespace
        /// </summary>
        /// <returns>A compiled regular expression instance.</returns>
        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        /// <summary>
        /// Gets a compiled regular expression that matches season and episode patterns in the format S##E## or
        /// S##E##E## (for double episodes).
        /// </summary>
        /// <returns>A compiled <see cref="Regex"/> instance that matches season/episode identifiers with case-insensitive
        /// matching.</returns>
        [GeneratedRegex(@"S\d{2}E\d{2}(E\d{2})?", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex SeasonEpisodeRegex();

        /// <summary>
        /// Gets a regex that matches a title followed by a four-digit year in parentheses from the beginning of a
        /// string.
        /// </summary>
        /// <returns>A compiled <see cref="Regex"/> instance.</returns>
        [GeneratedRegex(@"^(.*?\(\d{4}\))")]
        private static partial Regex TitleYearRegex();

        /// <summary>
        /// Matches trailing episode information in the format ' - S##E##' at the end of a string.
        /// </summary>
        /// <returns>A <see cref="Regex"/> instance for matching the pattern.</returns>
        [GeneratedRegex(@"\s-\sS\d+E\d+.*$", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex TrailingEpisodeRegex();

        /// <summary>
        /// Gets a compiled regular expression that matches trailing metadata in the format " - text" at the end of a
        /// line.
        /// </summary>
        /// <returns>A compiled regular expression instance.</returns>
        [GeneratedRegex(@"\s-\s.*$")]
        private static partial Regex TrailingMetadataRegex();

        #endregion

    }
}