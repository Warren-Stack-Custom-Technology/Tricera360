# Tricera360

Use DualShock 4 and DualSense controllers as virtual Xbox 360 controllers on Windows.

Tricera360 is a portable Windows x64 app. It reads DualShock 4, DualSense, and DualSense Edge controllers over USB or Bluetooth and maps their inputs to virtual Xbox 360 controllers. It is useful for games that recognize Xbox controllers and miss the original PlayStation pad.

## Getting started

1. Install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) if it is missing.
2. Download `Tricera360-v0.6.0-win-x64.zip` from [GitHub Releases](https://github.com/Warren-Stack-Custom-Technology/Tricera360/releases/latest), then extract it into a writable folder.
3. Run `Tricera360.exe` and connect your controllers by Bluetooth or USB. The Xbox output activates when the app receives input.
4. Select a controller to preview input, assign a layout, or turn its Xbox output off if the game already reads the physical pad.

The portable ZIP contains an unsigned executable. Windows SmartScreen may display an unrecognized-app warning. The release includes `SHA256SUMS.txt` so you can compare your download with the published ZIP, and the build commands below let you build it yourself.

## Profiles and multiplayer

- **Default** is read-only. Create custom button and trigger mappings, swap the two stick axes, or invert either physical stick axis per controller. The Xbox preview reflects the mapped stick output.
- The `Profiles` folder lives beside the executable. Copy the app folder to take your layouts with you. Windows can identify the same physical controller differently on another PC, so it may need its layout assigned again.
- Tricera360 can create up to eight virtual devices. XInput addresses four controllers. Playing with five to eight requires a game that supports additional controllers through another input API.

## Build and test

Use the .NET 8 SDK from the repository root.

```powershell
dotnet run --project Tests/Tricera360.Tests.csproj -c Release
dotnet publish Tricera360.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

To test virtual device creation on Windows, quit Tricera360 and run `.\Tricera360.exe --test-outputs` from the folder containing the executable. The test reports how many virtual devices accepted input and how many got XInput slots. Close the result dialog to disconnect the test devices.

## License

Tricera360 is released under the [MIT License](LICENSE). Notices and terms for bundled dependencies are in [Licenses](Licenses/).
