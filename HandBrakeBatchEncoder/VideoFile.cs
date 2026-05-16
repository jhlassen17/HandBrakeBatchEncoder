using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HandBrakeBatchEncoder
{
    /// <summary>
    /// Descriptor Class that encapsulates information about a video file to be processed, 
    /// including its file path, normalized title and episode information,
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
        /// Gets or sets the subtitle-related command-line arguments.
        /// </summary>
        public string SubtitleArguments { get; protected set; } = string.Empty;

        /// <summary>
        /// Gets or sets the HDR optimization-related command-line arguments.`
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

        /// <summary>
        /// Utility method that uses ffprobe to analyze the video file and determine if it contains HDR content.
        /// </summary>
        protected void GenerateHDRArguments()
        {
            // Call the helper method to calculate HDR arguments based on ffprobe analysis
            string hdrArgs = CalculateRes(this.FilePath);

            // If we got any HDR-related arguments back, save them to the property
            if (!string.IsNullOrWhiteSpace(hdrArgs))
            {
                this.HDRArguments = hdrArgs;
            }
        }

        /// <summary>
        /// Determines whether the ffprobe output indicates HDR content.
        /// </summary>
        /// <param name="ffprobeOutput">The ffprobe output to analyze.</param>
        /// <returns>true if HDR10 (PQ) or HLG transfer characteristics are detected; otherwise, false.</returns>
        protected static bool IsHdr(string ffprobeOutput)
        {
            // Detect HDR10 (PQ) or HLG
            return HDRRegex().IsMatch(ffprobeOutput);
        }

        /// <summary>
        /// Parses ffprobe output to extract video resolution dimensions.
        /// </summary>
        /// <param name="ffprobeOutput">The raw output from ffprobe containing width and 
        /// height information.</param>
        /// <returns>A tuple containing the width and height in pixels, or (0, 0) if the 
        /// values are not found.</returns>
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

        /// <summary>
        /// Calculates the appropriate HDR optimization arguments based on ffprobe analysis of the video file.
        /// </summary>
        /// <param name="file">Path to the video file</param>
        /// <returns>HDR optimization arguments if HDR content is detected; otherwise, an empty string.</returns>
        protected static string CalculateRes(string file)
        {
            // Set up ffprobe arguments to get color transfer, primaries, and
            // resolution info for the first video stream
            var args = $"-v error -select_streams v:0 " +
            $"-show_entries stream=color_transfer,color_primaries,width,height " +
            $"-of default=noprint_wrappers=1 \"{file}\"";

            // Run ffprobe and capture the output
            var probeResult = HBEState.RunProcess(HBEState.FFProbePath, args);

            // If ffprobe failed to analyze the file, log a warning and return no HDR arguments
            if (string.IsNullOrWhiteSpace(probeResult))
            {
                Console.WriteLine("⚠️  Warning: ffprobe failed to analyze the file. Proceeding without HDR optimizations or resolution checks.");
                return string.Empty;
            }

            // Check if the output indicates HDR content based on color transfer characteristics
            if (IsHdr(probeResult))
            {
                Debug.WriteLine("HDR content detected. Adding HDR optimization arguments.");
                return "--hdr10-opt";
            }

            // If we got here, no HDR indicators were found. We could add additional logic here to check resolution and apply
            // --large-file if desired, but for now we'll just return no HDR arguments.
            return string.Empty;
        }

        /// <summary>
        /// Generates subtitle command-line arguments based on the preferred subtitle track and assigns them to
        /// SubtitleArguments.
        /// </summary>
        protected void GenerateSubtitleArguments()
        {
            // Get the subtitle that we want to add
            var (trackNum, manual) = GetPreferredSubtitleTrack();

            // Decide whether to explicitly set the subtitle, or accept all eng ones
            string subtitleArgs = manual
                ? $"--subtitle {trackNum ?? 1} --subtitle-default=1"
                : "--subtitle-lang-list eng --all-subtitles --subtitle-default=1 --first-subtitle";

            // Save the generated arguments to the property
            this.SubtitleArguments = subtitleArgs;
        }

        /// <summary>
        /// Utility function that tries to get the first English or Undetermined track and 
        /// returns its Handbrake index, along with a force flag
        /// </summary>
        /// <param name="file">Path to video file</param>
        /// <returns>Nullable track number and manual search flag</returns>
        protected (int? trackNumber, bool forceManual) GetPreferredSubtitleTrack()
        {
            // Set up scanner arguments to get subtitle info
            string args = $"-v error --scan -i \"{this.FilePath}\"";
            string output = HBEState.RunProcess(@"C:\Program Files\HandBrake\HandBrakeCLI.exe", args, Debugger.IsAttached);

            // Extract lines that describe subtitles
            var subtitleLines = output.Split('\n')
                .Where(l => l.Contains("Subtitle:", StringComparison.OrdinalIgnoreCase))
                .Select(l => l.Trim())
                .ToList();

            // Check for no match
            if (subtitleLines.Count == 0)
                return (null, false);

            // HandBrake uses 1‑based numbering for --subtitle N
            var numberedSubs = subtitleLines
                .Select((line, index) => (line, index: index + 1))
                .ToList();

            // Prefer explicit SDH English (case‑insensitive match)
            var sdh = numberedSubs.FirstOrDefault(s =>
                s.line.Contains("sdh", StringComparison.OrdinalIgnoreCase) &&
                (s.line.Contains("(eng)", StringComparison.OrdinalIgnoreCase) ||
                 s.line.Contains("english", StringComparison.OrdinalIgnoreCase)));

            // If we found an explicit SDH English track, return it with the manual flag set to true
            if (sdh.line != null)
                return (sdh.index, true);     // "manual" because we pick a specific track

            // Fallback to first clearly English subtitle
            var eng = numberedSubs.FirstOrDefault(s =>
                s.line.Contains("(eng)", StringComparison.OrdinalIgnoreCase) ||
                s.line.Contains("english", StringComparison.OrdinalIgnoreCase) ||
                s.line.Contains("(iso639-2: eng)", StringComparison.OrdinalIgnoreCase));

            // If we found a clearly English track, return it with the manual flag set to false to indicate we can be more flexible
            if (eng.line != null)
                return (eng.index, false);

            // Last fallback: single unlabeled subtitle (likely English)
            if (subtitleLines.Count == 1)
                return (1, true);

            // Multiple subs, no obvious choice → let normal "English list" logic handle it
            return (null, false);

        }

        /// <summary>
        /// Checks whether the media file has been processed by verifying the presence and value of a metadata tag.
        /// </summary>
        /// <returns><see langword="true"/> if the processed tag exists with the expected value; otherwise, <see
        /// langword="false"/>.</returns>
        protected bool HasProcessedTag()
        {
            // Use ffprobe to get the value of the processed tag in JSON format
            string args = $"-v quiet -print_format json -show_format \"{this.FilePath}\"";
            // Run ffprobe and capture the output
            string output = HBEState.RunProcess(HBEState.FFProbePath, args, Debugger.IsAttached);
            // Look for COPYRIGHT tag
            var match = Regex.Match(output, @"""TAG:" + HBEState.ProcessedTagName + """\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);

            // If we don't find the tag, or if the value doesn't match our expected processed value, return false
            if (!match.Success)
                return false;

            // Extract the tag value and compare it to our expected processed value (case-insensitive)
            string value = match.Groups[1].Value;

            // Return true if the value matches our expected processed tag value, ignoring case
            return value.Equals(HBEState.ProcessedTagValue, StringComparison.OrdinalIgnoreCase);
        }

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
