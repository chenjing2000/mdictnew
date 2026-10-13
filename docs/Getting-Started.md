# Getting started

Use Windows x64 with a compatible .NET 10 Windows Desktop Runtime x64. End users do not need the SDK; the ordinary .NET Runtime does not include WPF. WebView2 Runtime is also required.

Launch dist/MdictViewer.exe or the EXE downloaded from GitHub Releases in a writable directory. Use the application's folder/dictionary selector to choose your own content. Libraries, dictionaries, and recordings are external user data.

Settings are stored beside the EXE as dictionaries.json. See [Configuration](Configuration.md) for naming and recovery. Close the program before replacing the EXE and preserve existing user files.

Developers need .NET 10 SDK (honor global.json where present). Use [Testing](Testing.md) for verification and [Publishing](Publishing.md) to build the portable EXE. Start with Core checks, then run UI/Full checks serially on the desktop.

## Detailed operation

- [Architecture](architecture.md)
- [Dictionary resources](local-resources.md)
- [Single-file implementation](single-file-deployment.md)