# Portable release - 2026-10-13

Windows x64, framework-dependent single EXE. Install .NET 10 Windows Desktop Runtime x64 before running. Also requires WebView2 Runtime.

- Unified Portable.pubxml and tools/Publish.ps1; output: dist/MdictViewer.exe.
- Staged publication validates a single EXE and preserves existing user configuration and other user files.
- No bundled .NET runtime, trimming, single-file compression, separate PDBs, or XML documentation.
- Source, documentation, and publishing regression tests are included in this tag; user data and local media are excluded.

See [health check](Health-Check-2026-10-13.md) for the actual local validation results and limitations. Release builds also run the publishing safeguard tests; they do not replace the local UI/audio checks.
The release moves the EXE to dist/MdictViewer.exe. Run tools/Publish.ps1 to copy the old configuration from dist/MdictViewer when its old EXE is still present. Conflicting configurations stop publication. The original configuration remains as a backup after migration.
