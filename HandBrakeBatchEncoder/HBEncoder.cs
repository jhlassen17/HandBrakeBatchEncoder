using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HandBrakeBatchEncoder
{
    /// <summary>
    /// FFmpeg NVENC encoder class. Handles (re)encoding of a video file using ffmpeg
    /// in place of the previous HandBrakeCLI implementation.
    /// </summary>
    public partial class HBEncoder
    {
        #region Constructors

        /// <summary>
        /// Default constructor
        /// </summary>
        public HBEncoder()
        {
            //
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// The current state of the application
        /// </summary>
        public required HBEState State { get; init; }

        #endregion

        #region Public Methods

        /// <summary>
        /// Encodes the given video file using ffmpeg with the preset determined by the current state.
        /// </summary>
        /// <param name="video">The video file to encode</param>
        /// <returns>True on success, false on failure</returns>
        public bool EncodeVideo(VideoFile video)
        {
            bool result = false;
            string arguments = this.GetFFmpegArgsForVideo(video);
            if (!string.IsNullOrEmpty(arguments))
            {
                result = RunFFmpegCLI(arguments, video);
            }
            // If we were successful, flag the video file
            if (result) SetMkvCopyrightTag(video);
            return result;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Builds the ffmpeg CLI argument string for the given video file based on the
        /// current encoding mode and video properties (HDR, subtitles).
        ///
        /// HandBrake → ffmpeg translation notes:
        ///   -e nvenc_h265           → -c:v hevc_nvenc
        ///   --encoder-preset p4     → -preset p4
        ///   -q 26                   → -cq 26          (NVENC constant quality)
        ///   --maxWidth/Height       → -vf scale=...   (scale filter)
        ///   --aencoder av_aac       → -c:a aac
        ///   --mixdown stereo        → -ac 2
        ///   --ab 160                → -b:a 160k
        ///   --all-audio             → -map 0:a (all audio streams)
        ///   --audio-copy-mask ...   → -c:a copy
        ///   --audio-fallback aac    → (handled by -c:a copy for common codecs)
        ///   --audio-lang-list eng   → -map 0:a:m:language:eng?
        ///   --subtitle ...          → -map 0:s:... -c:s copy  (see SubtitleArguments)
        ///   --hdr10-opt             → -color_trc smpte2084 -color_primaries bt2020 -colorspace bt2020nc
        ///   --color-matrix bt2020ncl→ -colorspace bt2020nc
        ///   --optimize              → -movflags +faststart
        ///   --markers               → -map_chapters 0
        ///   --vfr                   → -vsync vfr
        ///   --encoder-profile main10→ -profile:v main10
        ///   --encoder-level 5.1     → -level:v 5.1
        ///   -f av_mkv               → output filename with .mkv extension (format auto-detected)
        ///   --crop 0:0:0:0          → (no crop; omitted — ffmpeg default)
        /// </summary>
        protected string GetFFmpegArgsForVideo(VideoFile video)
        {
            string subtitleArgs = video.SubtitleArguments;
            string hdrArgs = video.HDRArguments;
            List<string> parts;

            switch (this.State.EncodeMode)
            {
                // ── XR Mode ───────────────────────────────────────────────────────────────
                // Optimised for XR glasses: 1080p cap, stereo AAC, BT.2020 colour matrix.
                case EncodingOptions.XRMode:
                    parts =
                    [
                        // Input
                        $"-i \"{video.FilePath}\"",
                        "-y",                                           // Overwrite without prompting

                        // Stream mapping
                        "-map 0:v:0",                                   // First video stream
                        "-map 0:a:m:language:eng?",                     // English audio only (? = optional)

                        // Video codec
                        "-c:v hevc_nvenc",                              // NVENC H.265 (= HB nvenc_h265)
                        "-preset p4",                                   // NVENC preset p4 ≈ HB "p4" (medium)
                        "-cq 26",                                       // Constant quality  (= HB -q 26)

                        // Scale to 1920×1080 max; \, escapes the comma inside min() for ffmpeg
                        @"-vf ""scale=min(iw\,1920):min(ih\,1080):force_original_aspect_ratio=decrease""",

                        // Audio codec
                        "-c:a aac",                                     // AAC  (= HB av_aac)
                        "-ac 2",                                        // Stereo downmix  (= HB --mixdown stereo)
                        "-b:a 160k",                                    // 160 kbps  (= HB --ab 160)

                        // Colour / container
                        "-colorspace bt2020nc",                         // BT.2020 colour matrix (= HB --color-matrix bt2020ncl)
                        "-movflags +faststart",                         // Streaming optimisation (= HB --optimize)
                        "-map_chapters 0",                              // Preserve chapter markers (= HB --markers)
                    ];

                    if (!string.IsNullOrWhiteSpace(subtitleArgs))
                        parts.Add(subtitleArgs);

                    parts.Add($"\"{video.OutputFilePath}\"");
                    break;

                // ── Standard Mode ─────────────────────────────────────────────────────────
                // Full-quality encode: up to 4K, copy audio streams, HDR metadata preserved.
                case EncodingOptions.StandardPreset:
                    parts =
                    [
                        // Input
                        $"-i \"{video.FilePath}\"",
                        "-y",

                        // Stream mapping
                        "-map 0:v:0",
                        "-map 0:a:m:language:eng?",                     // English audio (optional)
                        "-map 0:a:m:language:und?",                     // Undetermined audio (often English; optional)

                        // Video codec
                        "-c:v hevc_nvenc",                              // NVENC H.265
                        "-preset slow",                                 // = HB --encoder-preset slow
                        "-cq 19",                                       // = HB -q 19
                        "-profile:v main10",                            // = HB --encoder-profile main10
                        "-level:v 5.1",                                 // = HB --encoder-level 5.1
                        "-vsync vfr",                                   // Variable frame rate (= HB --vfr)

                        // Scale to 3840×2160 max
                        @"-vf ""scale=min(iw\,3840):min(ih\,2160):force_original_aspect_ratio=decrease""",

                        // Audio — copy all streams that ffmpeg can mux into MKV
                        // (covers aac, ac3, eac3, truehd, dts, dtshd, mp3, flac — same list as HB --audio-copy-mask)
                        "-c:a copy",

                        // Container
                        "-movflags +faststart",                         // = HB --optimize
                        "-map_chapters 0",                              // = HB --markers
                    ];

                    if (!string.IsNullOrWhiteSpace(subtitleArgs))
                        parts.Add(subtitleArgs);

                    // HDR metadata flags (= HB --hdr10-opt)
                    if (!string.IsNullOrWhiteSpace(hdrArgs))
                        parts.Add(hdrArgs);

                    parts.Add($"\"{video.OutputFilePath}\"");
                    break;

                default:
                    return string.Empty;
            }

            return string.Join(" ", parts);
        }

        /// <summary>
        /// Runs ffmpeg with the given arguments and streams progress to the console.
        ///
        /// Unlike HandBrake (which wrote progress to stdout), ffmpeg writes ALL output —
        /// including progress — to stderr. Progress lines are identified by containing
        /// both "frame=" and "fps=".
        /// </summary>
        protected static bool RunFFmpegCLI(string arguments, VideoFile video)
        {
            var psi = new ProcessStartInfo
            {
                FileName = HBEState.FFmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = psi };

            // ffmpeg writes everything — banner, stream info, progress — to stderr.
            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                if (e.Data.Contains("frame=") && e.Data.Contains("fps="))
                {
                    string formatted = FormatProgressLine(e.Data);
                    var (title, episode) = (video.Title.Title, video.Title.Episode);
                    string display = episode != null ? $"{title} [{episode}]" : title;
                    int padWidth = Math.Max(0, Console.WindowWidth - display.Length - 5);
                    Console.Write($"\r[{display}]  {formatted.PadRight(padWidth)}");
                }
                else if (e.Data.StartsWith("Error", StringComparison.OrdinalIgnoreCase)
                      || e.Data.Contains("No such file or directory")
                      || e.Data.Contains("Invalid argument")
                      || e.Data.Contains("Conversion failed"))
                {
                    Console.Error.WriteLine($"\n⚠️  {e.Data}");
                }
            };

            // ffmpeg stdout is usually empty when writing to a file, but capture it anyway.
            process.OutputDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (e.Data.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
                    Console.Error.WriteLine($"\n⚠️  {e.Data}");
            };

            // Start the process and begin reading output asynchronously
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit(7200000);

            // Return true if ffmpeg exited with code 0 (success), false otherwise
            return process.ExitCode == 0;
        }

        /// <summary>
        /// Parses an ffmpeg size string (e.g. "512KiB", "1234MiB") into bytes,
        /// then re-formats it at the largest unit that keeps the value ≥ 1.
        /// Thresholds: GiB ≥ 1 GiB, MiB ≥ 1 MiB, KiB ≥ 1 KiB, else bytes.
        /// </summary>
        private static string FormatSize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw;

            raw = raw.Trim();

            int splitAt = -1;
            for (int i = 0; i < raw.Length; i++)
            {
                if (char.IsLetter(raw[i]))
                {
                    splitAt = i;
                    break;
                }
            }
            if (splitAt <= 0) return raw;

            if (!double.TryParse(raw[..splitAt].Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out double value))
                return raw;

            string unit = raw[splitAt..].Trim().ToUpperInvariant();

            double bytes = unit switch
            {
                "KIB" or "KB" => value * 1024,
                "MIB" or "MB" => value * 1024 * 1024,
                "GIB" or "GB" => value * 1024 * 1024 * 1024,
                "TIB" or "TB" => value * 1024 * 1024 * 1024 * 1024,
                _ => value
            };

            const double GiB = 1024d * 1024 * 1024;
            const double MiB = 1024d * 1024;
            const double KiB = 1024d;

            return bytes switch
            {
                >= GiB => $"{bytes / GiB:F2} GiB",
                >= MiB => $"{bytes / MiB:F1} MiB",
                >= KiB => $"{bytes / KiB:F0} KiB",
                _ => $"{bytes:F0} B"
            };
        }

        /// <summary>
        /// Replaces ffmpeg's raw "size=NNNkB" token in a progress line with a
        /// properly scaled value (KiB/MiB/GiB) via FormatSize.
        /// </summary>
        private static string FormatProgressLine(string line)
        {
            return SizeRegex().Replace(line, m =>
            {
                string raw = m.Groups[1].Value;
                if (raw.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                    return m.Value; // leave "size=N/A" alone

                return $"size={FormatSize(raw)}";
            });
        }

        /// <summary>
        /// Creates a temporary MKV tag XML file marking the output as processed.
        /// </summary>
        protected static string CreateTagXml()
        {
            string tempName = Path.GetRandomFileName();
            string path = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(tempName, "xml"));
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
            File.WriteAllText(path, xml);
            return path;
        }

        /// <summary>
        /// Sets the COPYRIGHT=processed MKV tag on the output file via mkvpropedit,
        /// allowing subsequent runs to detect already-processed files.
        /// </summary>
        protected static void SetMkvCopyrightTag(VideoFile video)
        {
            try
            {
                string tagFile = CreateTagXml();
                string args = $"\"{video.OutputFilePath}\" --tags global:\"{tagFile}\"";
                string result = HBEState.RunProcess(HBEState.MKVPropEditPath, args, Debugger.IsAttached);
                if (Debugger.IsAttached) Debug.WriteLine(result);
                File.Delete(tagFile);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\n⚠️  {ex.Message}");
            }
        }

        /// <summary>
        /// Regex to match ffmpeg's "size=NNNkB" token in progress lines, 
        /// capturing the raw size value for reformatting.
        /// </summary>
        /// <returns></returns>
        [GeneratedRegex(@"size=\s*(\S+)")]
        private static partial Regex SizeRegex();

        #endregion
    }
}