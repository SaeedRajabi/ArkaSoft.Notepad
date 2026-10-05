# Code Protection (Release Hardening)

This document explains how ArkaSoft Notepad protects its code from reverse
engineering when users download the published binaries, and what the honest
limits of that protection are.

## What is enabled

### 1. Automatic obfuscation on every Release build

The project integrates [Obfuscar](https://github.com/obfuscar-obfuscator/obfuscar)
via the `ObfuscateRelease` MSBuild target (`ArkaSoft.Notepad.UI.csproj`) and the
WPF-safe rule set in `Src/ArkaSoft.Notepad.UI/obfuscate.xml`.

| Protection | Effect |
|---|---|
| Private/internal symbol renaming | Decompiled output loses every meaningful identifier — methods, fields, and helper classes appear as `a`, `b`, `#a1` … |
| String encryption (`HideStrings`) | Resource keys, file paths, registry paths, and internal messages no longer exist as readable text inside the binary; they are decrypted at runtime |
| Debug info dropped (`RegenerateDebugInfo=false`) | No PDBs, no original method/line mapping |

Verified against the shipped binary: names such as `ApplyTypedTextDirection`,
`SaveSessionSnapshot`, `WireFindPanel` and strings such as `settings.json`
or the registry paths are **no longer present** in the compiled DLL.

### 2. WPF-safe rules (why public API is kept)

WPF resolves many things by *name at runtime*: `{Binding}` expressions,
`x:Static` command references, compiled BAML, `{DynamicResource}` keys, and
`System.Text.Json` serialization of settings. Renaming public members breaks
all of these. The configuration therefore keeps the public surface intact
and scrambles everything private/internal — which is where the application
logic lives.

### 3. No debug artifacts

Release builds set `DebugType=none` / `DebugSymbols=false`, so no `.pdb`
files with method/line mappings are ever shipped.

### 4. One-command protected publish

`Scripts/publish-protected.ps1` publishes the single compressed executable,
verifies the obfuscation on the pre-bundle assembly (fails the build if any
private symbol or plaintext settings path leaks), and prints the result:

```powershell
powershell -ExecutionPolicy Bypass -File Scripts\publish-protected.ps1
# ==> DONE: publish\ArkaSoft.Notepad.exe
```

Unicode renamed identifiers (`UseUnicodeNames=true`) are enabled: decompiled
identifiers appear as unreadable unicode soup instead of `a/a1/a2` patterns.

This yields a single compressed executable (obfuscation runs automatically
during publish, because publish invokes the Release build). With
`--self-contained true` no runtime is needed on the target machine.
## Honest limits

- **Nothing is mathematically irreversible.** Any program that runs on a
  user's machine can, in principle, be analyzed. The goal is to make the
  effort so large that it is not worth it.
- Public API names remain visible (required for WPF to work). A determined
  attacker can still map the outer shape of the application; the
  implementation details behind it are what become unreadable.
- IL-level control-flow obfuscation, anti-tamper, and anti-debugging are
  beyond Obfuscar's scope. If stronger protection is ever required, a
  commercial obfuscator (for example .NET Reactor or SmartAssembly) can be
  added on top without changing the application code.
- **NativeAOT** (which removes IL entirely) is not supported by WPF and is
  therefore not an option for this project.

## Verification

After each Release build, `bin/Release/net10.0-windows/ArkaSoft.Notepad.dll`
is replaced with the protected binary (`obj/obfuscated` holds a copy). A
quick sanity check:

```bash
# these must return 0
grep -ac "ApplyTypedTextDirection" ArkaSoft.Notepad.dll
grep -ac "settings.json" ArkaSoft.Notepad.dll

# these must return 1 (public API intact)
grep -ac "GetLineColumn" ArkaSoft.Notepad.dll
```

And launch the exe once to confirm the encrypted strings decrypt correctly
at runtime.
