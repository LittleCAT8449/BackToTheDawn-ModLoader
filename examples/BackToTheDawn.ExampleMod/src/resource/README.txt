Place ExampleMod-owned assets in this directory.

Build-And-Deploy.ps1 copies this directory to:
BepInEx/mods/BackToTheDawn.ExampleMod/resource

The loader does not interpret resource formats yet; the mod can load them
using normal file APIs from that directory.
