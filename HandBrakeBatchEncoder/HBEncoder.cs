using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace HandBrakeBatchEncoder
{
    /// <summary>
    /// HandBrake CLI Encoder class.  Handles the (re)encoding of a 
    /// video file
    /// </summary>
    public class HBEncoder
    {
        #region Instance Properties

        //


        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor
        /// </summary>
        public HBEncoder()
        {
            //
        }

        ///// <summary>
        ///// State-dependent construcotor
        ///// </summary>
        ///// <param name="state">The current state of the app</param>
        //public HBEncoder(HBEState state) : this()
        //{
        //    // Save it
        //    this.State = state;
        //}

        #endregion

        #region Public Properties

        /// <summary>
        /// The current state of the application
        /// </summary>
        public required HBEState State { get; init; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Utility method that encodes the given video file 
        /// into the file's preferred format
        /// </summary>
        /// <param name="video">The video file to encode</param>
        /// <returns>Success flag, true</returns>
        public bool EncodeVideo(VideoFile video)
        {
            // Set up vars
            bool result = false;
            // Get the arguments
            string arguments = this.GetHandBrakePresetForVideo(video);
            // Make sure that we got some arguments
            if (arguments != null)
            {
                // Run the encoder
                result = RunHandBrakeCLI(arguments, video);
            }
            // If we were successful, flag the video file
            if (result) SetMkvCopyrightTag(video);
            return result; 
        }

        #endregion


        #region Utility Methods

        /// <summary>
        /// Utility method that generates the appropriate HandBrake CLI arguments for encoding 
        /// the given video file based on the current encoding mode in the state and the properties 
        /// of the video file (such as whether it has HDR content or subtitles).
        /// </summary>
        /// <param name="video">The video file for which to generate HandBrake CLI arguments.</param>
        /// <returns>A string containing the HandBrake CLI arguments.</returns>
        protected string GetHandBrakePresetForVideo(VideoFile video)
        {
            // Build HandBrake CLI arguments
            string arguments;
            string subtitleArgs = video.SubtitleArguments;
            string hdrArgs = video.HDRArguments;

            // Determine arguments based on encoding mode
            switch (this.State.EncodeMode)
            {
                // XR Mode: optimized for playback on XR glasses, which can be picky about certain encoding features
                // and often require more aggressive compression to fit within storage and performance constraints
                case EncodingOptions.XRMode:
                    arguments = string.Join(" ", new string[]
                    {
                        // Input & Output
                        $"-i \"{video.FilePath}\"",
                        $"-o \"{video.OutputFilePath}\"",
                        "-f av_mkv",                // Force MKV output (for better compatibility with XR glasses, which can be picky about MP4 features) - we will remux to MP4 later if needed for TV content
                        // Video
                        "-e nvenc_h265",           // Use NVENC H.265 (HEVC)
                        "--encoder-preset p4",   // Preset 4 (roughly "medium") for good quality/speed balance
                        "-q 26",                    // Reasonable quality/compression
                        "--maxWidth 1920",         // Force 1080p max width
                        "--maxHeight 1080",        // Force 1080p max height
                        // Audio
                        "--audio-lang-list eng",
                        "--aencoder av_aac",       // Convert to AAC
                        "--mixdown stereo",        // Stereo downmix
                        "--ab 160",                // Bitrate for AAC
                        // Subtitles
                        subtitleArgs, 
                        // Misc
                        "--color-matrix bt2020ncl",     // Set color matrix for BT.2020 content
                        "--optimize",             // Optimize for streaming
                        "--markers",              // Keep chapter markers
                    });
                    break;
                case EncodingOptions.StandardPreset:
                    // Standard encoding without XR‑specific settings
                    arguments = string.Join(" ", new string[]
                    {
                            // Input & Output
                            $"-i \"{video.FilePath}\"",
                            $"-o \"{video.OutputFilePath}\"",
                            "-f av_mkv",                // Force MKV output (for better compatibility with XR glasses, which can be picky about MP4 features) - we will remux to MP4 later if needed for TV content
                            // Video
                            "-e nvenc_h265",           // Use NVENC H.265 (HEVC)
                            "--encoder-preset slow",   // Preset 4 (roughly "medium") for good quality/speed balance
                            "-q 19",                    // Reasonable quality/compression 
                            "--encoder-profile main10", // Use Main10 profile for better HDR support (if the source is HDR)
                            "--encoder-level 5.1",      // Set level to 5.1 for better compatibility with older devices (many can't handle the default "auto" level for HEVC)
                            "--vfr",                    // Use variable frame rate (VFR) to preserve original timing (important for TV content with mixed frame rates)
                            "--maxWidth 3840",          // Force 4K max width (for 4K content, but allow smaller for TV)
                            "--maxHeight 2160",         // Force 4K max height (for 4K content, but allow smaller for TV)
                            "--crop 0:0:0:0",           // No cropping
                            // Audio
                            "--all-audio",
                            "--audio-lang-list eng,und",    // Prefer English tracks, but allow Undetermined (often used for English tracks that just aren't labeled properly)
                            "--audio-copy-mask aac,ac3,eac3,truehd,dts,dtshd,mp3,flac",     // Try to copy any English or Undetermined track that is in a common format
                            "--audio-fallback aac",     // Fallback to AAC if no copyable track is found
                            // Subtitles
                            subtitleArgs,
                            // Misc
                            "--optimize",               // Optimize for streaming
                            "--markers"                 // Keep chapter markers
                    });
                    if (!string.IsNullOrWhiteSpace(hdrArgs))
                    {
                        arguments += " " + hdrArgs; // Add HDR optimization arguments if applicable
                    }
                    break;
                default:
                    return string.Empty;    // No encoding for "None" mode, so return empty string to indicate that we should skip this file
            }

            // Return the generated arguments
            return arguments;
        }

        /// <summary>
        /// Utility function that runs HandBrakeCLI with the specified arguments
        /// </summary>
        /// <param name="arguments">The CLI arguments to use with HandBrake</param>
        /// <param name="fileName">The full name of the file that we are working on</param>
        /// <exception cref="Exception">Handbrake errors, if any</exception>
        protected static bool RunHandBrakeCLI(string arguments, VideoFile video)
        {
            // Set up start info
            ProcessStartInfo psi = new()
            {
                FileName = HBEState.HandBrakePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Set up process
            using var process = new Process { StartInfo = psi };

            // Set up handling for output data
            process.OutputDataReceived += (s, e) =>
            {
                // Get out of here if there is nothing
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                // HandBrake prints progress lines starting with "Encoding"
                if (e.Data.StartsWith("Encoding:", StringComparison.OrdinalIgnoreCase))
                {
                    // Move to the beginning of the current console line and overwrite
                    var (title, episode) = (video.Title.Title, video.Title.Episode);

                    // Make the display look nice
                    string display = episode != null
                        ? $"{title} [{episode}]"
                        : title;

                    // Update display
                    Console.Write($"\r[{display}]  {e.Data.PadRight(Console.WindowWidth - display.Length - 5)}");
                }
                // Optional: display other messages for context
                else if (e.Data.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    // Error messages from HandBrake start with "ERROR", so we can check for those and display them prominently
                    Console.WriteLine($"\n⚠️  {e.Data}");
                }
            };

            // Set up handling of error data
            process.ErrorDataReceived += (s, e) =>
            {
                // Get out of here if there is nothing
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                // Check for error messages and display them
                if (e.Data.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    // Error messages from HandBrake start with "ERROR", so we can check for those and display them prominently
                    Console.WriteLine($"\n⚠️  {e.Data}");
                }
            };

            // Start process
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Wait until it is done
            process.WaitForExit();

            // Check the exit code
            if (process.ExitCode != 0)
            {
                // Fail
                return false;
            }
            else
            {
                // Success
                return true;
            }
        }

        /// <summary>
        /// Utility method that creates the "processed" tag flag in the output 
        /// MKV file so that us and other apps know that the video has been encoded
        /// </summary>
        /// <remarks>
        /// Tag name = "COPYRIGHT", value = "processed" means that the file 
        /// has already been handled
        /// </remarks>
        /// <returns>Path to the XML for the tag</returns>
        protected static string CreateTagXml()
        {
            // File stuff
            string tempName = Path.GetRandomFileName();
            string path = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(tempName, "xml"));
            // Tag XML
            string xml = @"<?xml version=""1.0""?>
                        <Tags>
                          <Tag>
                            <Targets />
                            <Simple>
                              <Name>COPYRIGHT</Name>
                              <String>processed</String>
                            </Simple>
                          </Tag>
                        </Tags>";
            // Save
            File.WriteAllText(path, xml);
            return path;
        }

        /// <summary>
        /// Sets the copyright tag in the specified MKV video file using an external tool.
        /// </summary>
        /// <remarks>This method uses the MKVPropEdit tool to apply copyright metadata to the MKV file.
        /// The method creates a temporary tag XML file, applies it to the video, and then deletes the temporary file.
        /// If a debugger is attached, process output and errors are written to the debug output window.</remarks>
        /// <param name="video">The video file for which to set the copyright tag. Must not be null and should have a valid output file
        /// path.</param>
        protected static void SetMkvCopyrightTag(VideoFile video)
        {
            try
            {
                // Create the lag flag file
                string tagFile = CreateTagXml();
                // Set up args
                string args = $"\"{video.OutputFilePath}\" --tags global:\"{tagFile}\"";
                // Run the tool
                string result = HBEState.RunProcess(HBEState.MKVPropEditPath, args, Debugger.IsAttached);
                // Debug
                if (Debugger.IsAttached) Debug.WriteLine(result);
                // Should we also apply the tag to the source file?
                    //args = $"\"{video.FilePath}\" --tags global:\"{tagFile}\"";
                    //result = this.State.RunProcess(this.State.MKVPropEditPath, args, Debugger.IsAttached);
                    //if (Debugger.IsAttached) Debug.WriteLine(result);
                // Delete the old tag file
                File.Delete(tagFile);
            }
            catch (Exception ex)
            {
                // It gave me error
                Debug.WriteLine($"\n⚠️  {ex.Data}");
                //throw;
            }
        }

        #endregion

    }
}
