# Far Horizons Science

A science-expansion mod for **Kerbal Space Program 1.12.x** built around deep space exploration:
drill cores out of other worlds, catch the solar wind, scoop dust from comets, analyze it all in flight,
photograph distant worlds with real saved photos, drop probes into gas giants, crash spent stages for
seismology, and send probes to the edge of interstellar space.

## What's in it

### Physical samples (bring them home, or analyze on board)
| Source | Experiment | How |
|---|---|---|
| **Any surface drill** (stock + modded, via ModuleManager) | Shallow Regolith Core | Land, deploy the drill, hold still for 90 s. 1 EC/s. Per-biome. |
| **Drill-O-Matic** (large drill) | Deep Stratigraphic Core | Same, 5 minutes at 3 EC/s. Worth twice as much. |
| **GSC-1 Solar Wind Collector** | Solar Wind Ion Capture | Deploy in space. Fill rate scales with **1/distance² from the sun**: about 12 h at Kerbin's distance and under 2 h near Moho. Inside a magnetosphere (10 planetary radii of any planet with an atmosphere) it's 50x slower. |
| **AGL-2 Aerogel Dust Collector** | Interplanetary Dust Capture | Deploy in space. Fastest in solar orbit, 0.3x inside a planet's SOI. |
| ...flown within 50 km of a **comet** | **Cometary Coma Particles** | Fills 6x faster, and each sample is worth 3x as much. |

Samples have low transmit value (10–15%). Collectors **must be retracted before entering an atmosphere or landing**, or the sample is contaminated and lost.

### On-board analysis
The **SAL-1 Automated Sample Laboratory** (uncrewed) and the stock **Mobile Processing Lab** (crewed, 2x speed) find
unanalyzed samples anywhere on the vessel and produce a separate, **100%-transmittable analysis** for the
same location. **The original sample is not consumed**, so you can still bring it home for its own science.
Each scientist aboard speeds the analysis up per experience level. Analysis continues while the vessel is unloaded.

| Sample | Analysis | Base time |
|---|---|---|
| Regolith Core | Regolith Core Analysis | 2 h |
| Deep Core | Stratigraphic Sequence Analysis | 6 h |
| Solar Wind | Solar Isotope Analysis | 4 h |
| Interplanetary Dust | Dust Grain Analysis | 3 h |
| Comet Dust | Cometary Material Analysis | 6 h |

### Long-duration surveys
These need hours of continuous observation. Progress **pauses** (it isn't lost) when conditions break, and continues while you're off flying other missions.

| Part | Survey | Conditions |
|---|---|---|
| **HPX-7 Heliospheric Explorer** | Galactic Cosmic Ray Survey | In space, outside any magnetosphere, 6 h |
| | **Termination Shock Crossing** (80 sci) | Solar orbit, ≥ 6x Kerbin's distance from the sun (~82 Gm), 12 h |
| | **Heliopause Observation** (200 sci) | Solar orbit, ≥ 9x Kerbin's distance (~122 Gm, beyond Eeloo's apoapsis), 24 h |
| **LFR-3 Radio Receiver** | Low-Frequency Radio Sky Survey | Outside Kerbin's SOI and ≥ 2 Gm from Kerbin (orbit **or** surface), 6 h |
| **SEIS-2 Seismometer** | Long-Period Seismic Monitoring | Landed and perfectly still for a Kerbin day |

### Science cameras 📷
Cameras have a **live viewfinder window** (zoom, filters, crosshair, plus a readout of what's in frame and whether it's worth science).
**Every photo is saved as a real PNG** in `[KSP folder]/Screenshots/FarHorizons/`, taken from the camera's own point of view.

**Visual mods appear in photos.** Scatterer atmospheres and oceans, EVE clouds, TUFX post-processing and anything else that hooks KSP's cameras will show up.
Photos are rendered through the game's *real* camera pipeline: for a few frames the main view jumps to the science camera's viewpoint with the UI hidden (a quick "shutter blink"),
then the finished frame is captured and your view is restored. In map view or IVA, photos fall back to a private offscreen render, which skips the visual-mod effects.
Tune this in `GameData/FarHorizons/Settings.cfg`:
- `photoMode = Auto | Offscreen`. Offscreen means no blink, but no mod visuals.
- `settleFrames` (default 8). Raise it if volumetric clouds look smeared.
- `superSize` (1–4). Renders at a multiple of screen resolution.
The camera looks along the part's "up" axis (away from the surface it's attached to).

| Part | Zoom (FOV) | Filters | Resolution |
|---|---|---|---|
| **FHC-1 Wide-Field Survey Camera** (basicScience) | 60° / 35° / 20° | Natural, Enhanced, Monochrome | 1920x1080 |
| **FHT-4 Long-Range Imaging Telescope** (scienceTech) | 10° / 4° / 1.5° / 0.5° / 0.15° | + False Color (IR), Solar H-alpha | 2560x1440 |

What's in frame decides the science:
| Shot | Experiment | Notes |
|---|---|---|
| Anything, while landed/splashed | Surface Panorama | per-biome |
| Pointing down (≤ 25° off nadir) from low orbit / high flight | Orbital Surface Imaging | per-biome |
| Another world filling enough of the frame | Telescopic Imaging | 25% of frame (FHC-1) / 15% (FHT-4). Zoom in or get closer |
| A comet or asteroid nearby (10 km FHC-1 / 50 km FHT-4) | Comet Nucleus / Asteroid Close-up | |
| The sun through the **H-alpha** filter | H-alpha Solar Imaging | **Without the filter at ≤ 5° FOV the sensor burns out.** An EVA engineer can replace it |
| Kerbin, from **> 50 Gm** away (FHT-4) | **Distant Portrait of Kerbin** (100 sci) | The "pale blue dot" shot |

### Atmospheric entry probe
**ADP-1 Atmospheric Descent Profiler.** Arm it and drop it into any atmosphere. It logs 20 altitude bands as it falls. Full value at 70% of the atmosphere profiled, and partial profiles earn partial science.
When it reaches 90% of its heat limit it **auto-transmits whatever it has** before it dies. It's built for Jool and Eve dives (2600 K rated).

### Kinetic impactor seismology
Every command part (probe cores, pods) gets a hidden **kinetic impactor** module. Crash it into a world at **≥ 5 MJ** (1 t at 100 m/s) and
**every landed SEIS-2 seismometer on that world records the impact**, even if its vessel is unloaded on the other side of the planet.
Bigger impacts (up to 60 MJ) give more science. Plant a seismometer, then aim your spent stages at it.

### Radio occultation
**RSX-1 Radio Science Transponder.** Orbit any world with an atmosphere except Kerbin, arm it, and wait for Kerbin to set behind the limb.
For 20 s of the line of sight to Kerbin passing through the atmosphere, the fading signal profiles the air. Needs an antenna. Keep time warp ≤ 10x near the event.

### Instant instruments (stock-style)
| Part | Experiment | Where |
|---|---|---|
| FH-MAG1 Fluxgate Magnetometer | Magnetometer Reading | High atmosphere, any orbit; per-biome in low orbit |
| PWR-2 Plasma Wave Receiver | Plasma Wave Spectrum | Any orbit |
| NS-4 Neutron Spectrometer | Neutron Spectrometry (water-ice mapping) | Low orbit, per-biome |
| AMS-1 Atmospheric Mass Spectrometer | Atmospheric Mass Spectrometry | Atmospheric flight, per-biome |

All 29 experiments have hand-written result text for most bodies in the system.

### Tech tree
basicScience → AMS-1, FHC-1 · electrics → FH-MAG1 · spaceExploration → PWR-2 · scienceTech → NS-4, FHT-4 ·
fieldScience → SEIS-2 · advExploration → GSC-1, ADP-1, RSX-1 · advScienceTech → AGL-2, SAL-1, LFR-3 · experimentalScience → HPX-7

## Suggested mission arc
1. **Early:** AMS-1 and the magnetometer on Kerbin flights; FHC-1 panoramas everywhere; regolith cores from the Mun and Minmus with a Drill-O-Matic Junior.
2. **Mid:** Land a seismometer on the Mun, then crash your next mission's spent stage into it. Build a Mun/Minmus base with the MPL analyzing cores and transmitting the results. Put a solar wind collector into a high orbit just outside Kerbin's magnetosphere. Use the FHT-4 to photograph every planet from high orbit.
3. **Interplanetary:** A Moho sun-diver for fast solar wind and H-alpha shots of the sun. Occultations and an entry probe at Duna and Eve. Seismometers on Duna, Laythe and Vall. Radio surveys from Tylo or Eeloo.
4. **Late:** Dive an ADP-1 into Jool. Intercept a comet with an aerogel collector and a camera. Then launch a lightweight HPX-7 + FHT-4 probe on a Jool-assisted escape trajectory: turn around past 50 Gm for the portrait of Kerbin, then keep going to the heliopause.

## Install
1. Install [ModuleManager](https://forum.kerbalspaceprogram.com/topic/50533-module-manager/) (needed for the drill and MPL patches).
2. Build the plugin (below), then copy `GameData/FarHorizons` into your KSP `GameData` folder.

## Building
Requires the .NET SDK (any recent version; targets .NET Framework 4.7.2 via reference-assembly packages) and a KSP 1.12 install to compile against.

```sh
./build.sh "/path/to/Kerbal Space Program"           # build -> GameData/FarHorizons/Plugins/FarHorizons.dll
./build.sh "/path/to/Kerbal Space Program" install   # build + copy into KSP's GameData
```
Windows, Linux, and macOS (`KSP.app`) install layouts are detected automatically.

### Installing through CKAN
To have CKAN track the mod (so it shows as installed and can be removed or upgraded there), package it and install the generated `.ckan` file instead of copying by hand:
```sh
./build.sh "/path/to/Kerbal Space Program"
./package-ckan.sh                                   # -> dist/FarHorizons-<version>.zip + dist/FarHorizons.ckan
ckan install --instance <name> -c dist/FarHorizons.ckan
```
CKAN won't overwrite files it doesn't own, so remove any hand-copied `GameData/FarHorizons` first. To push a rebuild, bump the version in `FarHorizons.version`, re-run `package-ckan.sh`, then `ckan upgrade --instance <name> -c dist/FarHorizons.ckan`.

## Layout
```
src/FarHorizons/
  FHSScienceModule.cs        base: data storage, review dialog, transmit, MPL, EVA "Take Data", recovery
  ModuleCoreSampler.cs       drill core sampling (patched onto drills)
  ModuleSampleAnalyzer.cs    on-board sample -> analysis lab
  ModuleExposureCollector.cs solar wind / dust / comet collectors
  ModuleDeepSpaceSurvey.cs   condition-gated long-duration surveys
  ModuleFHSCamera.cs         science cameras: viewfinder window, frame analysis, photo science
  FHSCameraRig.cs            offscreen camera-stack render (viewfinder/fallback), filters, PNG saving
  FHSPhotoCapture.cs         live capture through the real cameras so visual mods appear; Settings.cfg
  ModuleDescentProfiler.cs   atmospheric entry probe with emergency transmit
  FHSImpacts.cs              kinetic impactor + seismometer impact listener (works on unloaded vessels)
  ModuleRadioOccultation.cs  radio occultation geometry
  FHSUtil.cs                 environment model, subject parsing, helpers
GameData/FarHorizons/
  Resources/FHS_ScienceDefs.cfg   all 29 experiment definitions + result text
  Parts/*.cfg                     14 parts
  Patches/FHS_Drills.cfg          core samplers on drills
  Patches/FHS_Labs.cfg            sample analyzer in the MPL
  Patches/FHS_Impactors.cfg       kinetic impactor on all command parts
  Settings.cfg                    photo capture settings
```
Everything is tunable in the `.cfg` files. For example, the analyzer recipe format is `input>output@hours; ...`.

## Known limitations / TODO
- **Placeholder visuals:** the parts reuse stock science-sensor and Mystery Goo models (scaled). Custom models need Unity + KSP PartTools.
- Stock model paths and `node_attach` positions are written from memory of the 1.12 file layout. If a part doesn't appear, check `KSP.log` for "model not found" and fix the `MODEL { model = ... }` path.
- Background progress (unloaded vessels) doesn't simulate electric charge. Solar wind flux while unloaded is taken from the vessel's position when it was last loaded.
- The magnetosphere model is simple: any non-sun body with an atmosphere shields out to 10 radii.
- The live **viewfinder preview** uses cloned cameras, so it doesn't show visual-mod effects or filters. Saved photos do.
- Live photos are capped at screen resolution × `superSize` (they're center-cropped to the camera's aspect ratio, never upscaled).
- A mod that tracks the camera position from its own secondary camera, updated before rendering, could lag by a frame or two in photos. `settleFrames` covers most cases.
- Radio occultation and the descent profiler sample once per physics tick, so very high time warp can skip right past the event.
- English only (no localization files yet).

## License
MIT
