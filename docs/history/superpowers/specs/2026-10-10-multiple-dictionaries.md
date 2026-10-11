# Multiple Dictionaries Design

Approved in the conversation on 2026-10-10; implementation explicitly requested.

- Fixed search toolbar: text input, back, forward, settings. No search button or dictionary title above input.
- Enter and candidate confirmation search all dictionaries and retain input focus. Busy input is read-only, not disabled.
- Back/forward chevrons use #ba5b49 in rounded squares. Settings gear uses #3271ae in a circle. All button backgrounds use #f5f2e9.
- Settings edit, browse and remove up to eight ordered MDX paths. A plus below the last populated valid row adds another row. Save paths locally; cancel leaves current configuration intact.
- Separate vertical result sections with MDX filename titles and isolated dictionary readers/browser resources. One outer scrollbar; sections follow document heights, including image loading, folding and anchors.
- Enter, candidate and entry links update every section and share a 50-entry navigation history. Anchors remain local; F9 is not history. New lookup after Back discards forward history.
- Candidates merge in dictionary order, retain original keys, deduplicate case-insensitively, cap at 12, debounce 150 ms, cancel stale requests.
- Partial matches show a per-dictionary missing/error result. If all dictionaries miss, retain existing pages and history. Keep the established failure-input rule: manual/candidate searches retain edited input; failed entry links/history navigation restore the last displayed word. One unsupported dictionary must not break other dictionaries.
- Continue using current MDX/MDD readers; arbitrary format compatibility is not promised. Dispose browser controls/readers on configuration replacement and closure.
