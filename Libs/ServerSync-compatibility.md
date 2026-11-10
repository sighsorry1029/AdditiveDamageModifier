# Vendored ServerSync: Valheim 1.0.7 repair

The existing ServerSync 1.0.0.0 binary is retained with a narrowly scoped IL repair.
This is not an upstream library upgrade or a shared installation update.

- Original SHA-256 (repository commit `38f3494`, `Libs/ServerSync.dll`):
  `166956302A294E224474B26F4C7D58409084AD3F48BD0AF1FEB7551F229C8F60`
- Repaired SHA-256:
  `C3D070A51C918118A1B8A75265D6326DE66A03494E1F04B088E44CA6F103FFFD`
- Tool: Mono.Cecil 0.11.6.0, deterministic MVID.
- Target: original Windows client Valheim 1.0.7 `assembly_valheim.dll`, SHA-256
  `A5130F5A957AB51CB6538F5412CBE57B43F927F4A679918BFF199B5C905D01BC`.

`ZRoutedRpc.Everybody` is now `const long = 0`, not a static storage field.
Exactly three `ldsfld` instructions become `ldc.i8 0`: `ConfigSync.sendZPackage`,
the `AddConfigEntry` change callback, and the `AddCustomValue` change callback.
The 204 method contracts, instruction counts, branch destinations, configuration
wire format, RPC names, version checks, administrator checks, and buffering logic
are unchanged. Short branch ranges were checked after serialization. No game DLL
was modified or included in the mod. Normal builds still merge this binary with ILRepack.

To reproduce, obtain the original binary from the commit above in a separate path,
then run PowerShell 7:

```powershell
pwsh -NoProfile -File Libs/Patch-ServerSync.ps1 -InputAssembly <original.dll> -OutputAssembly <repaired.dll> -GameAssembly <original-1.0.7-assembly_valheim.dll> -CecilPath <Mono.Cecil-0.11.6.dll>
```

The script rejects unexpected input hashes, game constants, and repair counts.
It is a one-time maintenance tool, not a build dependency or runtime compatibility layer.
`Test-Mod.ps1` checks final merged IL operands, literal-field accesses, and the game
members used by ServerSync on both client and server assemblies. Its managed-only
ThreadingHelper fixture does not execute ServerSync's queued Harmony installation.
Live login, synchronization, admin/non-admin changes, version rejection, reconnect,
and Steam/PlayFab transport still require testing inside the game/Mono runtime.
