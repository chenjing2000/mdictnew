# Application overview

MdictViewer is a local Windows application built with C#, .NET 10, and WPF. It reads user-owned content and stores settings beside the executable. MdictViewer additionally uses WebView2; the other applications use their existing audio and WPF components.

The unified tooling wraps the existing test runners and portable publishing configuration. It does not replace the application algorithms or file formats. Test data is isolated from user data and UI/native-input checks run sequentially.

See [architecture.md](architecture.md) for the existing design, [Configuration](Configuration.md) for settings, [Testing](Testing.md) for verification, and [Publishing](Publishing.md) for distribution.