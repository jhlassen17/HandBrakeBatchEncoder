using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HandBrakeBatchEncoder
{
    /// <summary>
    /// Descriptor Class that encapsulates information about a video file to be processed, 
    /// including its file path, normalized title/episode information, and pre-built
    /// ffmpeg argument fragments for subtitles and HDR.
    /// </summary>
    public partial class VideoFile
    {

        #region Instance Properties

        // Backing field for caching processed status
        protected bool? _AlreadyProcessed = null;
        // Backing field for caching output existence status
        protected bool? _OutputExists = null;

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFile"/> class.
        /// </summary>
        protected VideoFile()
        {
            // Nothing to do at the moment
            this.FilePath = string.Empty;
            this.State = HBEState.Empty;
            this.Title = TitleDescriptor.Empty;
        }

        /// <summary>
        /// Initializes a new instance of the VideoFile class with the specified file path and state.
        /// </summary>
        /// <param name="filePath">The path to the video file.</param>
        /// <param name="hBEState">The Hand Brake Encoder state.</param>
        public VideoFile(string filePath, HBEState hBEState) : this()
        {
            // Sanity checks
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            // Save reference to state and file path
            this.FilePath = filePath!;
            this.State = hBEState ?? throw new ArgumentNullException(nameof(hBEState), "HBEState cannot be null.");

            // Update our information based on the file path
            RefreshInfo();
            // 
            // TODO: Init Title correctly
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFile"/> class with the specified file path and an empty
        /// state.
        /// </summary>
        /// <param name="filePath">The path to the video file.</param>
        public VideoFile(string filePath) : this(filePath, HBEState.Empty)
        {
            // This constructor allows creating a VideoFile with just a file path, using an empty state by default.
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the current state of the app
        /// </summary>
        public required HBEState State { get; init; }

        /// <summary>
        /// Gets the source file path.
        /// </summary>
        public string FilePath { get; protected set; }

        /// <summary>
        /// Gets or sets the output file path.
        /// </summary>
        public string? OutputFilePath { get; set; }

        /// <summary>
        /// Gets a value indicating whether the output file already exists.
        /// </summary>
        public bool OutputExists
        {
            get
            {
                // If we don't have an output path, we can't check for existence
                if (string.IsNullOrEmpty(this.OutputFilePath))
                    return false;

                // Return cached value if available
                if (!_OutputExists.HasValue)
                {
                    // Check if any existing MKV in the output directory matches our title and episode
                    string outputDir = Path.GetDirectoryName(this.OutputFilePath) ?? string.Empty;
                    bool alreadyExists = Directory
                            .EnumerateFiles(outputDir, "*.mkv")
                            .Select(f => HBEState.NormalizeTitle(new VideoFile(f, this.State) { State = this.State }))
                            .Any(existing =>
                                !(!string.Equals(existing.Title, this.Title.Title, StringComparison.OrdinalIgnoreCase) ||
                                this.Title.Episode != null && existing.Episode != this.Title.Episode)
                            );
                    // Save result to cache
                    _OutputExists = alreadyExists;
                }

                // Return cached result
                return _OutputExists.Value;
            }
        }

        /// <summary>
        /// Gets the title descriptor which contains the normalized title and 
        /// episode information extracted from the file name.
        /// </summary>
        public TitleDescriptor Title { get; protected set; }

        /// <summary>
        /// Gets a value indicating whether the item has already been processed.
        /// </summary>
        public bool AlreadyProcessed
        {
            get
            {
                // Return cached value if available
                if (_AlreadyProcessed.HasValue)
                    return _AlreadyProcessed.Value;
                // Figure it out
                _AlreadyProcessed = HasProcessedTag();
                // Return result
                return _AlreadyProcessed.Value;
            }
        }

        /// <summary>
        /// ffmpeg-style subtitle argument fragment, e.g.:
        ///   "-map 0:s:2 -c:s copy -disposition:s:0 default"
        ///   "-map 0:s:m:language:eng? -c:s copy -disposition:s:0 default"
        /// </summary>
        public string SubtitleArguments { get; protected set; } = string.Empty;

        /// <summary>
        /// ffmpeg-style HDR metadata argument fragment, e.g.:
        ///   "-color_trc smpte2084 -color_primaries bt2020 -colorspace bt2020nc"
        /// Empty string when source is SDR.
        /// </summary>
        public string HDRArguments { get; protected set; } = string.Empty;

        #endregion

        #region Public Methods

        /// <summary>
        /// Refreshes cached processing status, renormalizes the title, and 
        /// regenerates subtitle and HDR arguments.
        /// </summary>
        public void RefreshInfo()
        {
            // Clear cached processed status
            _AlreadyProcessed = null;
            _OutputExists = null;
            // Re-run normalization in case filename has changed
            this.Title = HBEState.NormalizeTitle(this);
            _AlreadyProcessed = HasProcessedTag();
            // Generate subtitle arguments based on current file state
            GenerateSubtitleArguments();
            // Generate HDR arguments based on current file state
            GenerateHDRArguments();
        }

        #endregion

        #region Private Methods

        // ── HDR ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs ffprobe and, if HDR content is detected, populates HDRArguments
        /// with the appropriate ffmpeg colour-metadata flags.
        /// </summary>
        protected void GenerateHDRArguments()
        {
            string hdrArgs = CalculateHDRArgs(this.FilePath);
            if (!string.IsNullOrWhiteSpace(hdrArgs))
                this.HDRArguments = hdrArgs;
        }

        /// <summary>
        /// Detects HDR content via ffprobe and returns the corresponding ffmpeg flags.
        ///
        /// HandBrake equivalent: --hdr10-opt
        ///
        /// For HDR10 (smpte2084 / PQ):
        ///   -color_trc smpte2084 -color_primaries bt2020 -colorspace bt2020nc
        /// For HLG (arib-std-b67):
        ///   -color_trc arib-std-b67 -color_primaries bt2020 -colorspace bt2020nc
        /// </summary>
        protected static string CalculateHDRArgs(string file)
        {
            // Run ffprobe to get color metadata for the first video stream
            var args = $"-v error -select_streams v:0 " +
                       $"-show_entries stream=color_transfer,color_primaries,width,height " +
                       $"-of default=noprint_wrappers=1 \"{file}\"";

            var probeResult = HBEState.RunProcess(HBEState.FFProbePath, args);

            // If ffprobe fails, we log a warning and return empty args to avoid blocking the encoding process.
            if (string.IsNullOrWhiteSpace(probeResult))
            {
                Console.WriteLine("⚠️  Warning: ffprobe failed to analyse the file. Proceeding without HDR metadata.");
                return string.Empty;
            }

            // Check for HDR indicators in the ffprobe output and build the appropriate ffmpeg arguments.
            if (IsHdr(probeResult))
            {
                Debug.WriteLine("HDR content detected. Adding ffmpeg HDR colour metadata flags.");
                bool isHlg = probeResult.Contains("arib-std-b67", StringComparison.OrdinalIgnoreCase);
                string trc = isHlg ? "arib-std-b67" : "smpte2084";
                return $"-color_trc {trc} -color_primaries bt2020 -colorspace bt2020nc";
            }

            // No HDR detected, return empty string
            return string.Empty;
        }

        /// <summary>
        /// Returns true if ffprobe output indicates HDR10 (PQ) or HLG transfer characteristics.
        /// </summary>
        protected static bool IsHdr(string ffprobeOutput)
            => HDRRegex().IsMatch(ffprobeOutput);

        /// <summary>
        /// Parses ffprobe output for video resolution.
        /// </summary>
        /// <param name="ffprobeOutput">Raw ffprobe output containing lines like "width=3840" and "height=2160".</param>
        protected static (int width, int height) GetResolution(string ffprobeOutput)
        {
            // Initialize default values
            int width = 0, height = 0;

            // Use regular expressions to extract width and height values from ffprobe output
            var wMatch = WidthRegex().Match(ffprobeOutput);
            var hMatch = HeightRegex().Match(ffprobeOutput);

            // If matches are found, parse the numeric values and assign them to width and height
            if (wMatch.Success) width = int.Parse(wMatch.Groups[1].Value);
            if (hMatch.Success) height = int.Parse(hMatch.Groups[1].Value);

            // Return the extracted resolution as a tuple
            return (width, height);
        }

        // ── Subtitles ────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds the ffmpeg subtitle argument fragment and assigns it to SubtitleArguments.
        ///
        /// HandBrake equivalents:
        ///   manual   → --subtitle N --subtitle-default=1
        ///   auto     → --subtitle-lang-list eng --all-subtitles --subtitle-default=1 --first-subtitle
        /// </summary>
        protected void GenerateSubtitleArguments()
        {
            // Get preferred subtitle track info from ffprobe analysis
            var (trackIndex, manual, hasEnglish) = GetPreferredSubtitleTrack();

            // Build ffmpeg subtitle arguments based on the analysis results
            if (manual && trackIndex.HasValue)
            {
                // Specific 0-based subtitle stream index found (e.g. SDH English at index 2)
                this.SubtitleArguments =
                    $"-map 0:s:{trackIndex.Value} -c:s copy -disposition:s:0 default";
            }
            else if (!manual && hasEnglish)
            {
                // Map all English subtitle streams; ? makes the map optional (no error if absent)
                this.SubtitleArguments =
                    "-map 0:s:m:language:eng? -c:s copy -disposition:s:0 default";
            }
            else
            {
                // No suitable subtitles found — omit subtitle args entirely
                this.SubtitleArguments = string.Empty;
            }
        }

        /// <summary>
        /// Uses ffprobe JSON output to inspect subtitle streams and return the
        /// preferred track selection.
        ///
        /// Replaces the previous HandBrakeCLI --scan approach.
        ///
        /// Returns:
        ///   trackIndex  — 0-based subtitle stream index for ffmpeg -map 0:s:N
        ///   forceManual — true  → use -map 0:s:N (specific track)
        ///                 false → use -map 0:s:m:language:eng? (all English)
        ///   hasEnglish  — true if at least one English subtitle stream exists
        /// </summary>
        protected (int? trackIndex, bool forceManual, bool hasEnglish) GetPreferredSubtitleTrack()
        {
            // Run ffprobe to get subtitle stream info in JSON format
            string args = $"-v quiet -print_format json -show_streams -select_streams s \"{this.FilePath}\"";
            string output = HBEState.RunProcess(HBEState.FFProbePath, args, Debugger.IsAttached);

            // Make sure we got valid output before trying to parse it
            if (string.IsNullOrWhiteSpace(output))
                return (null, false, false);

            try
            {
                // Parse the ffprobe JSON output to find subtitle stream information
                using var doc = JsonDocument.Parse(output);

                // Check if the "streams" array exists in the JSON output
                if (!doc.RootElement.TryGetProperty("streams", out var streams))
                    return (null, false, false);

                // Iterate through subtitle streams to find English and SDH tracks
                int count = streams.GetArrayLength();
                if (count == 0)
                    return (null, false, false);

                int? sdhEngIndex = null;
                int? firstEngIndex = null;

                for (int i = 0; i < count; i++)
                {
                    var stream = streams[i];
                    string lang = GetStreamTagValue(stream, "language");
                    string title = GetStreamTagValue(stream, "title");

                    bool isEnglish = lang.Equals("eng", StringComparison.OrdinalIgnoreCase);
                    bool isSdh = title.Contains("sdh", StringComparison.OrdinalIgnoreCase);

                    if (isEnglish && isSdh && !sdhEngIndex.HasValue)
                        sdhEngIndex = i;

                    if (isEnglish && !firstEngIndex.HasValue)
                        firstEngIndex = i;
                }

                // Priority 1: SDH English → specific track, manual
                if (sdhEngIndex.HasValue)
                    return (sdhEngIndex, true, true);

                // Priority 2: First clearly-English track → manual (exact index)
                if (firstEngIndex.HasValue)
                    return (firstEngIndex, false, true);

                // Priority 3: Single unlabelled subtitle → assume English, manual
                if (count == 1)
                    return (0, true, true);

                // Multiple subs, none clearly English → no subtitle args
                return (null, false, false);
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Error parsing ffprobe subtitle JSON: {ex.Message}");
                return (null, false, false);
            }
        }

        // ── Processed-tag check ──────────────────────────────────────────────────────────

        /// <summary>
        /// Uses ffprobe to check whether the MKV file has the COPYRIGHT=processed tag
        /// set by a previous encoding run.
        ///
        /// Note: the original regex was malformed for JSON output. Fixed here.
        /// ffprobe -print_format json produces: "COPYRIGHT": "processed"
        /// </summary>
        protected bool HasProcessedTag()
        {
            string args = $"-v quiet -print_format json -show_format \"{this.FilePath}\"";
            string output = HBEState.RunProcess(HBEState.FFProbePath, args, Debugger.IsAttached);

            if (string.IsNullOrWhiteSpace(output))
                return false;

            // Match: "COPYRIGHT": "processed"  (case-insensitive)
            var match = Regex.Match(
                output,
                $"\"{HBEState.ProcessedTagName}\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase);

            if (!match.Success)
                return false;

            return match.Groups[1].Value.Equals(HBEState.ProcessedTagValue, StringComparison.OrdinalIgnoreCase);
        }

        // ── Helpers ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Safely reads a named tag value from an ffprobe stream JsonElement.
        /// Returns empty string if the element or tag is absent.
        /// </summary>
        private static string GetStreamTagValue(JsonElement stream, string tagName)
        {
            if (stream.TryGetProperty("tags", out var tags) &&
                tags.TryGetProperty(tagName, out var value))
            {
                return value.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        // ── Generated Regexes ────────────────────────────────────────────────────────────

        /// <summary>
        /// Gets a regular expression that matches HDR transfer function identifiers.
        /// </summary>
        /// <returns>A regular expression that matches "smpte2084" or "arib-std-b67" (case-insensitive).</returns>
        [GeneratedRegex(@"smpte2084|arib-std-b67", RegexOptions.IgnoreCase, "en-US")]
        private static partial Regex HDRRegex();

        /// <summary>
        /// Matches 'width=' followed by one or more digits.
        /// </summary>
        /// <returns>A compiled regex instance.</returns>
        [GeneratedRegex(@"width=(\d+)")]
        private static partial Regex WidthRegex();

        /// <summary>
        /// Gets a regular expression that matches "height=" followed by one or more digits.
        /// </summary>
        /// <returns>A compiled Regex instance.</returns>
        [GeneratedRegex(@"height=(\d+)")]
        private static partial Regex HeightRegex();

        #endregion
    }
}