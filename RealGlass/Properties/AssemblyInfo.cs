using System.Reflection;
using System.Runtime.InteropServices;
using RealGlass;

[assembly: AssemblyTitle("Real Glass")]
[assembly: AssemblyDescription("Realistic glass for Cities: Skylines' buildings: reflections of the city and the sky, real glass tints. Requires Harmony.")]
[assembly: AssemblyProduct("RealGlass")]
[assembly: ComVisible(false)]

// The release is Mod.Version; the wildcard fills the fourth part with the time of day
// (seconds / 2), which gives every build a distinct assembly identity -- what lets the
// game load a rebuilt DLL instead of the copy it already has cached. Paired with
// <Deterministic>false</Deterministic> in the .csproj -- it does nothing without it.
[assembly: AssemblyVersion(Mod.Version + ".*")]
[assembly: AssemblyInformationalVersion(Mod.Version)]
