# Configuration

The current configuration file is dictionaries.json beside the actual executable. It follows the EXE directory rather than the working directory. For AudioPlayer, ReciteWords, and ReadArticles, renaming the EXE changes the .settings.json stem; MdictViewer always uses dictionaries.json.

dictionaries.json contains up to eight MDX paths beside the EXE. Missing files remain listed; malformed configuration is reported. Opening Settings initializes a missing file.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [architecture.md](architecture.md) and [Testing](Testing.md). MdictViewer's legacy folder migration and conflict handling are described in [Publishing](Publishing.md).