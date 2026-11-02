# MelonLoader 0.6.1 reference assemblies

These three DLLs are copied verbatim from the official MelonLoader **0.6.1** release:

| file | assembly version |
|---|---|
| `MelonLoader.dll` | 0.6.1.0 |
| `0Harmony.dll` | 2.10.1.0 |
| `Tomlet.dll` | 5.1.2.0 |

Source: https://github.com/LavaGang/MelonLoader/releases/download/v0.6.1/MelonLoader.x64.zip
(taken from the `MelonLoader/net35` folder of that archive)

## Why vendor them

MelonLoader 0.6.1 ships **net35 and net6 only** — there is no `net472` flavor (0.7 added it).
Most GHPC installs are now 0.7.x, whose `net472` folder is *not* the same API surface
(0Harmony 2.10.2, Tomlet 6.2.0, plus MonoMod.Backports), so compiling against the local game
folder silently produced a DLL that a 0.6.1 user cannot load.

`I:\GHPC\NatoERA` therefore builds against these pinned 0.6.1 assemblies by default, so a plain
`dotnet build -c Release` is the DLL for a 0.6.1 (Mono/net35) install.

## Building against a different MelonLoader

Do **not** edit the HintPaths — override the path (and flavor, where the project has one):

```
# 0.6.1 downloaded elsewhere
/p:MelonLoaderRoot="C:\MelonLoader\0.6.1\MelonLoader"

# the game's own install: 0.7+ Mono flavor
/p:MelonLoaderRoot="E:\SteamLibrary\steamapps\common\Gunner HEAT PC\Bin\MelonLoader" /p:MelonApi=net472
```

(The exact property names are documented in the project file itself; AMP uses `MelonDir`.)

Keep the folder in sync with the MelonLoader your players actually run: the mod compiles fine
against any of them, but the *reference versions* end up in the DLL's assembly refs, which is
what decides whether a mismatch shows up as a clean warning or as a `MissingMethodException`.

## What the mod checks at runtime

`MlCompat.Report()` runs first in `OnInitializeMelon` and logs:

* the loaded `MelonLoader` version and its file path, plus `Environment.Version` (the CLR);
* a warning when that version is older than 0.6.1 or newer than the API the DLL was built on;
* a warning when the DLL was loaded out of a `net6` folder;
* a warning when the install is **mixed** — e.g. `Bin\MelonLoader\net35\MelonLoader.dll` is
  0.6.1 while `net472\MelonLoader.dll` is 0.7.3. That state produces the
  "Your mono runtime and class libraries are out of sync" and
  "cant resolve internal call to MelonLoader.BootstrapInterop::..." lines in the game log;
  the fix is to delete `Bin\MelonLoader`, `dobby.dll` and `version.dll` and reinstall one release.
