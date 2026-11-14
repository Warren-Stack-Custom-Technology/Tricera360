# Tricera360

<img width="578" height="452" alt="image" src="https://github.com/user-attachments/assets/187cebb4-3dc2-4bf5-9ef3-a94e3310796f" />

Use DualShock 4 and DualSense controllers as virtual Xbox 360 controllers on Windows.

Tricera360 is a portable Windows x64 app. It reads DualShock 4, DualSense, and DualSense Edge controllers over USB or Bluetooth and maps their inputs to virtual Xbox 360 controllers. It is useful for games that recognize Xbox controllers and miss the original PlayStation pad.

## Getting started

1. Install [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) if it is missing.
2. Download the `win-x64.zip` release asset from [GitHub Releases](https://github.com/Warren-Stack-Custom-Technology/Tricera360/releases/latest), then extract it into a writable folder.
3. Run `Tricera360.exe` and connect your controllers by Bluetooth or USB. The Xbox output activates when the app receives input.
4. Select a controller to preview input, assign a layout, or turn its Xbox output off if the game already reads the physical pad.

The portable ZIP contains an unsigned executable. Windows SmartScreen may display an unrecognized-app warning. The release includes `SHA256SUMS.txt` so you can compare your download with the published ZIP, and the build commands below let you build it yourself.

## Profiles and multiplayer

- **Default** is read-only. Create custom button and trigger mappings, swap the two stick axes, or invert either physical stick axis per controller. The Xbox preview reflects the mapped stick output.
- The `Profiles` folder lives beside the executable. Copy the app folder to take your layouts with you. Windows can identify the same physical controller differently on another PC, so it may need its layout assigned again.
- Use **Theme** at the bottom of the window to switch between Green, Yellow, Brown, Purple / Magenta, Black, and White. The selected theme is saved in `Profiles`.
- Tricera360 can create up to eight virtual devices. XInput addresses four controllers. Playing with five to eight requires a game that supports additional controllers through another input API.

## Automatic updates

The portable Windows build asks on first launch whether to install updates automatically. Change the choice with **Automatic updates** below the controller list. When enabled, Tricera360 checks GitHub Releases at startup and reopens after installing a verified update. If a virtual controller is active, the app defers the update until the next launch. Profiles stay in the portable folder. Versions earlier than 0.6.3 need a manual upgrade to get this feature.

When moving from an older build, keep the existing `Profiles` folder beside `Tricera360.exe`. If the old build stored it in its single-file extraction directory, copy that build's `Profiles` folder beside the new executable before first launch. By default those extraction directories are under `%TEMP%\.net\Tricera360`.

## Build and test

Use the .NET 8 SDK from the repository root.

```powershell
dotnet run --project Tests/Tricera360.Tests.csproj -c Release
dotnet publish Tricera360.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true
```

To test virtual device creation on Windows, quit Tricera360 and run `.\Tricera360.exe --test-outputs` from the folder containing the executable. The test reports how many virtual devices accepted input and how many got XInput slots. Close the result dialog to disconnect the test devices.

### Signing updates

On the maintainer's Windows account, run `powershell -ExecutionPolicy RemoteSigned -File scripts/Sign-Update.ps1 -Initialize` once. Commit the generated `Assets/UpdatePublicKey.xml` and build the portable ZIP after adding it. The private key stays in that Windows user's cryptographic key store. Continue signing subsequent releases from the same Windows account with that key. If the key is lost, users need to install a build with the replacement public key manually. The updater is disabled in builds made without the public key.

For each release, run `powershell -ExecutionPolicy RemoteSigned -File scripts/Sign-Update.ps1 -ZipPath path\to\Tricera360-v0.6.3-win-x64.zip`. Attach `Tricera360-update.json` and `Tricera360-update.sig` beside the Windows ZIP before publishing the release. The signed manifest authenticates the version, hash, and size of `Tricera360.exe`. Automatic updates replace that executable only.

[Optional donation](https://www.buymeacoffee.com/WarrenStack)

## License

Tricera360 is released under the [MIT License](LICENSE). Notices and terms for bundled dependencies are in [Licenses](Licenses/).
