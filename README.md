# KSP Hover Slam

KSP Hover Slam is a Kerbal Space Program 1 flight addon for Falcon 9-style booster recovery. It commands throttle, attitude, and landing gear for a suicide burn / hover slam using the active vessel's current mass, available engine thrust, surface velocity, and radar altitude.

## Features

- Follow the current suborbital trajectory and land near the natural impact point.
- Guided target mode with manual latitude/longitude entry, current vessel position capture, or current ballistic impact capture.
- Optional auto-deorbit assist that burns retrograde until a ballistic impact exists.
- Suicide-burn throttle calculation with touchdown speed and burn margin controls.
- Retrograde/grid-fin steering with target bias for lateral correction.
- Landing gear deployment by radar altitude.
- In-flight IMGUI panel, toggleable with `Alt+H`.

## Install

Build the DLL, then copy `GameData/KSPHoverSlam` into your KSP 1 install's `GameData` folder.

For the Steam path used while developing this mod:

```powershell
.\build.ps1
.\install.ps1 -KspRoot "C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program"
```

If Windows blocks local scripts, run them through `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1` and repeat for `install.ps1`.

The final installed layout should include:

```text
Kerbal Space Program/
  GameData/
    KSPHoverSlam/
      KSPHoverSlam.version
      Plugins/
        KSPHoverSlam.dll
```

## Usage

1. Launch a controllable booster with working engines, control authority, and landing gear.
2. Open the KSP Hover Slam window in flight with `Alt+H`.
3. Choose a guidance mode:
   - `Follow trajectory`: land on the current suborbital path.
   - `Guided target`: enter/capture a landing latitude and longitude.
4. Click `Arm hover slam`.
5. Keep the craft stable and let the addon command throttle, grid fins/control surfaces, and gear.

Quick-save before testing. The built-in impact predictor is intentionally lightweight and ignores aerodynamic drag, lift, staging changes, and rotating-body precision effects. For best results, start with suborbital hops or already-deorbited boosters, then tune touchdown speed and burn margin for your vehicle.

## Build

`build.ps1` compiles directly against the assemblies in a KSP 1 installation. If your KSP path differs, pass `-KspRoot`.

```powershell
.\build.ps1 -KspRoot "D:\Games\Kerbal Space Program"
```

There is also an SDK-style `.csproj` for IDEs. The PowerShell build is the most reliable path because it references KSP's own `mscorlib`, `System`, Unity, and `Assembly-CSharp` assemblies.

## GitHub

Repository: <https://github.com/Minecraftman04/KSP-Hover-Slam>

## License

MIT. See `LICENSE`.
