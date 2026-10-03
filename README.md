# HoDPro Compat

A small client-side compatibility mod for [Vintage Story](https://www.vintagestory.at/) that makes
**Hydrate or Diedrate** (HoD) and **Prosequor** share a HUD without the nutrition deficit meter
drifting out of line with the saturation bar.

## The problem

HoD draws an orange nutrition deficit bar on top of the vanilla saturation bar. Prosequor raises
the player's max saturation, so the saturation bar's scale changes (for example 1856 or 1912
instead of 1500).

The two bars draw their partition lines differently:

- **Vanilla** draws `min(50, (max - min) / interval)` dividers, evenly spaced across the full
  bar width, with none at the end.
- **HoD** draws a divider at every multiple of its interval (100), including the end of the bar.

When max saturation is not a multiple of 100, the two sets of dividers no longer line up, and the
deficit bar overlaps the saturation bar incorrectly.

## What the mod does

Every 100 ms on the client, the mod:

1. Finds the vanilla saturation bar and HoD's deficit bar.
2. Copies the saturation bar's min and max onto the deficit bar.
3. Converts the saturation bar's divider spacing into the interval HoD needs to draw dividers in
   the same places.

HoD is reached by reflection, so the mod has no compile-time dependency on it. If HoD's HUD
class or fields change, the mod logs an error and does nothing.

No dimensions, colors or config options are changed or added.

## Requirements

- Vintage Story 1.22.x
- [Hydrate or Diedrate](https://mods.vintagestory.at/hydrateordiedrate)
- Prosequor

The mod loads on the client only.

## Installing

Download `hodprocompat_<version>.zip` and put it in your `Mods` folder
(`%APPDATA%\VintagestoryData\Mods` on Windows).

## Building

Set the `VINTAGE_STORY` environment variable to your game install folder, then:

```
dotnet build -c Release
```

To produce a release zip (in `Releases/`):

```
./build.ps1
```

Debug launch profiles for the client and server are in
`HodProCompat/Properties/launchSettings.json`.

## Troubleshooting

With debug logging on, the client log shows a line each time the bar's scale is updated:

```
[HodProCompat] Deficit bar scale set to 0-1912, vanilla interval 100
```

## Layout

| Path | Contents |
| --- | --- |
| `HodProCompat/HodProCompatModSystem.cs` | The mod |
| `HodProCompat/modinfo.json` | Mod metadata and dependencies |
| `HodProCompat/assets/` | Asset origin used by the launch profiles and release build |
| `ZZCakeBuild/` | Cake build that validates JSON and packages the release zip |
