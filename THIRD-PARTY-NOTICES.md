# Third-party notices

## OpenUsage v0.7.12

AIUsage is an independent Windows implementation, inspired by and partially ported from [OpenUsage](https://github.com/robinebers/openusage/tree/v0.7.12). It is not an official OpenUsage or OpenAI release. Accounting/replay rules and selected pricing data were adapted; the SwiftUI interface, provider icons, analytics and updater were not copied.

See [docs/UPSTREAM.md](docs/UPSTREAM.md) for the source-file manifest and intentional differences.

### Original license, reproduced in full

MIT License

Copyright (c) 2026 Robin Ebers

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

## Microsoft .NET, WPF and Windows Forms

The self-contained downloads include Microsoft .NET and Windows Desktop components. The `runtime-notices` directory contains the full `LICENSE.TXT` and `THIRD-PARTY-NOTICES.TXT` from each of the official `dotnet/runtime`, `dotnet/wpf` and `dotnet/winforms` repositories, pinned to the runtime version selected by restore. `runtime-notices/sources.json` records source URLs and SHA-256 checksums.

`scripts/collect-notices.ps1` downloads this license text during packaging, not when the installed application runs. Packaging fails if the complete set cannot be retrieved. The r1 packaging revision adds this complete notice set; application behavior is unchanged from 0.1.0.

OpenAI, Codex, Windows and other product names belong to their respective owners. No affiliation or endorsement is implied.
