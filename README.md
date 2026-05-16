# HandBrakeBatchEncoder

Batch encode video files using **HandBrakeCLI** with smart automation, filtering, and configurable encoding settings.

## 🚀 Overview

**HandBrakeBatchEncoder** is a .NET-based utility that streamlines bulk video encoding by leveraging the power of HandBrakeCLI. It is designed to automate scanning, filtering, and encoding of large media libraries with minimal manual intervention.

This tool is ideal for:
- Media library organization
- Re-encoding large collections (e.g., Blu-ray rips, MKVs)
- Automating HandBrake workflows
- Reducing manual encoding effort

---

## ⚙️ Features

- ✅ Batch processing of video files
- ✅ Integration with **HandBrakeCLI**
- ✅ Configurable encoding presets
- ✅ Smart file filtering and selection
- ✅ Automatic output path handling
- ✅ Logging and error handling support
- ✅ Designed for large media collections

---

## 🧑‍💻 Requirements

- [.NET (6 or later recommended)](https://dotnet.microsoft.com/)
- [HandBrakeCLI](https://handbrake.fr/downloads2.php)

Ensure `HandBrakeCLI.exe` is installed and accessible (either in PATH or configured in the app).

---

## 📦 Installation

Clone the repository:

```bash
git clone https://github.com/jhlassen17/HandBrakeBatchEncoder.git
```

---

## Build the project:

```bash

dotnet build
```

---

## ▶️ Usage
Basic usage will depend on your configuration and input paths, but the general flow is:

1. Specify your source directory
2. Configure encoding options (preset, quality, etc.)
3. Run the batch encoder

Example (conceptual):
```
var encoder = new BatchEncoder(config);
encoder.Process();
```

---

## 🔧 Configuration

Typical configuration options may include:

- Source directory
- Destination/output directory
- Encoding preset (e.g., H.264, H.265)
- Quality settings
- File filters (extensions, size, date, etc.)
- Overwrite/re-encode rules

---

#3 🧠 How It Works
1. Scans the source directory for video files
2. Filters files based on rules (existing encodes, timestamps, etc.)
3. Invokes HandBrakeCLI for each file
4. Captures output and errors
5. Writes encoded files to destination

---

## 📁 Example Use Case
- Input: Blu-ray MKV files (~30–50 GB)
- Output: Compressed H.264/H.265 files (~5–10 GB)
- Result: Reduced storage usage while maintaining quality

---

## ⚠️ Notes
- Ensure file paths are valid and accessible
- HandBrakeCLI must be properly installed
- Large batch jobs may take significant time depending on hardware

---

## 🔮 Future Enhancements
- Progress reporting / UI
- Parallel encoding support
- Retry and failure recovery
- Config file support (JSON/YAML)
- Integration with media managers (e.g., Plex workflows)

---

## 🤝 Contributing
Contributions are welcome!

- Open issues for bugs or feature requests
- Submit pull requests for improvements

---

## 👨‍💻 Author

**Jeffrey Lassen**  
Version: `1.0.2.2`  
Last Updated: `05/16/2026`

https://github.com/jhlassen17

---

## 📄 License

This project is licensed under the MIT License.

See the full license in the LICENSE file.

---

## ☕ Support

If you find this useful:  
👉 https://buymeacoffee.com/hanf

---
