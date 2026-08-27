# Third-Party Notices

SQLExtended for SSMS includes third-party software. The relevant licences are reproduced below.

Three kinds of thing are listed here, and the distinction matters:

- **Vendored source** — someone else's code copied into this repository and compiled into the extension.
  Their licence governs it and is reproduced in full.
- **Redistributed binaries** — packages shipped inside the `.vsix`. Their licences are reproduced or named.
- **Credited work** — projects, articles and data sources this extension learned from without copying any
  code. Nothing here imposes a licence obligation; they are listed because the ideas were not ours.

---

## Statistics Parser (Brent Ozar Unlimited)

The statistics parsing engine behind **SQLExtended → Parse Statistics** (`Ctrl+K, Ctrl+G`) is vendored from the
Statistics Parser SSMS extension.

- Source: https://github.com/BrentOzarULTD/StatisticsParserExtension
- Vendored commit: `e1526b4ec20e7a102dbc1408a97a687812e14129` (2026-05-21)
- Vendored files:
  - `SQLExtended/Statistics/Core/**` — copied verbatim from `source/StatisticsParser.Core/`
    (`Models`, `Parsing`, `Formatting`), namespaces unchanged so upstream updates remain a file-for-file copy.
  - `SQLExtended.Tests/Statistics/**` — copied verbatim from `source/StatisticsParser.Core.Tests/`.
  - `SQLExtended/Statistics/Capture/**` — adapted from `source/StatisticsParser.Vsix/Capture/` (host plumbing
    changed to SQLExtended's package and logging; the brokered-service reflection logic is upstream's).

```
MIT License

Copyright (c) 2026 Brent Ozar Unlimited

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

## Redistributed binaries

These are packaged inside `SQLExtended.vsix` and installed alongside the extension. Everything else the
extension references (the Visual Studio SDK, SQL Server Management Objects, SqlClient's transitive
dependencies) is already present in SSMS and is deliberately not shipped.

| Component | Licence | Project |
|---|---|---|
| Newtonsoft.Json 13.0.4 — © James Newton-King 2008 | MIT | https://www.newtonsoft.com/json |
| AvalonEdit 6.3.1.120 — © 2000-2025 AlphaSierraPapa for the SharpDevelop Team | MIT | http://www.avalonedit.net/ |
| Microsoft.Data.SqlClient 7.0.1 — © Microsoft Corporation | MIT | https://aka.ms/sqlclientproject |
| Microsoft.SqlServer.TransactSql.ScriptDom 180.18.1 — © Microsoft Corporation | MIT | https://github.com/microsoft/SqlScriptDOM |
| System.Data.SQLite 1.0.119 (managed assembly and the x86/x64 `SQLite.Interop.dll`) | Public domain | https://system.data.sqlite.org/ |

The MIT licence text is reproduced above under Statistics Parser; it applies to each MIT component listed
here with that component's own copyright holder substituted.

SQLite and System.Data.SQLite are dedicated to the public domain by their authors rather than licensed —
see https://www.sqlite.org/copyright.html. No attribution is required; it is given anyway.

---

## Credited work

No code from any of the following is present in this repository. They are listed because the technique,
the design or the data came from them.

### SSMS-EnvTabs

The Environment Tabs feature (`EnvTabs/`) was inspired by SSMS-EnvTabs, which established that the VS 2026
shell's "colorize document tabs by regex" feature could be driven programmatically to colour SSMS query
tabs by connection.

- Source: https://github.com/Blake-goofy/SSMS-EnvTabs
- Licence: Apache License 2.0 — Copyright 2026 Blake Becker
  (https://www.apache.org/licenses/LICENSE-2.0)

**No code is shared, and the colouring mechanism is deliberately different.** SSMS-EnvTabs forces a chosen
colour by appending a regex comment to the pattern and brute-forcing it until the shell's internal
`HashHelpers.GetStableHashCode` of the pattern text lands on the wanted palette index. This implementation
instead calls the shell's own `FileColorService.SetFileColorAsync` to record an explicit
`groupId → colorIndex` mapping, which outranks the hash — so nothing internal has to be reimplemented, and
there is nothing to re-verify when Microsoft changes it. Because no part of that project is copied or
derived here, the Apache-2.0 attribution and NOTICE requirements are not triggered; the credit above is
given because the idea was theirs.

### SSMSBoost

The results-grid Aggregates pane (`ResultsGrid/Aggregates/`) is modelled on SSMSBoost's Aggregates window
and on Excel's status bar. SSMSBoost is a commercial product; nothing from it is included or decompiled.

- Product: https://www.ssmsboost.com/

### SQL Server build list

The build catalogue behind the Performance dashboard's Server info tab (`Monitoring/Performance/
SqlBuildData.cs`) is a generated snapshot of the community-maintained SQL Server build list, used to say
which servicing level an instance is on and where its release sits in the support lifecycle.

- Source: https://sqlserverbuilds.blogspot.com/
- The snapshot is regenerated by `SoluitionDocs/Tools/generate-sql-build-catalog.py`, and the generated
  file carries the date it was taken. The extension always reports that date rather than implying the list
  is current.

### Encrypted module decryption technique

The `WITH ENCRYPTION` recovery in `Decryption/` implements a long-published technique — that SQL Server
masks module text with a position-based keystream derived from the object's identity, so a second
ciphertext of the same object recovers the plaintext by XOR. The implementation here is original; the
technique is not.

- Reference: http://jongurgul.com/blog/sql-object-decryption/

### Microsoft SSMS and Visual Studio internals

Several features reach undocumented SSMS internals by reflection (the active connection, the Job
Properties dialog, the results grid, the Messages pane, tab colouring). Where behaviour had to be
established by decompiling shipped assemblies with ILSpy, that is recorded in the source and in CLAUDE.md.
No Microsoft code is copied into this repository.

SQL Server, SSMS and Visual Studio are trademarks of Microsoft Corporation. This extension is not
affiliated with or endorsed by Microsoft.
