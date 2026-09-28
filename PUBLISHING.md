# How to Update and Publish Screen Edge Light

## 1. Make Your Changes
Edit the code, fix bugs, or add features as normal. You can test your changes locally using:
```bash
dotnet run --project UbuntuEdgeLight.Linux/UbuntuEdgeLight.Linux.csproj
```

## 2. Update the Version
Bump the version in **both** places (keep them identical):
- `snap/snapcraft.yaml` → `version: '1.2.0'`
- `UbuntuEdgeLight.Linux/UbuntuEdgeLight.Linux.csproj` → `<Version>1.2.0</Version>`

The store rejects a version it has already received, so every upload needs a new number.

## 3. Rebuild the Package
Run the build command in the root of your project:
```bash
snapcraft pack
```
This creates a file such as `screen-edge-light_1.2.0_amd64.snap`.

## 4. Test Locally (Recommended)
```bash
sudo snap install ./screen-edge-light_1.2.0_amd64.snap --dangerous
```
Launch it from the **GNOME app menu** (not from a terminal running a conda/virtualenv environment,
which breaks snap startup). If something goes wrong, read the real error with:
```bash
journalctl --user --since "5 min ago" | grep screen-edge
```

## 5. Upload to the Store
Release to the **candidate** channel first. Only people who opted in to testing get it:
```bash
snapcraft upload --release=candidate screen-edge-light_1.2.0_amd64.snap
```
Once you're happy, promote that same revision to everyone (no rebuild needed):
```bash
snapcraft release screen-edge-light <revision> stable
```
(`snapcraft upload` prints the revision number; `snapcraft status screen-edge-light` shows it too.)

## 6. Store Listing
Screenshots, the banner, the video and the category are managed on the dashboard, not in
`snapcraft.yaml`: https://snapcraft.io/screen-edge-light/listing

Ready-made assets live in `store/`:
- `store/banner.png`: 1920×640 banner (source: `store/banner.html`)
- `store/icon-512.png`: PNG version of `snap/gui/icon.svg`

## 7. Verification
Users receive stable updates automatically in the background within a few hours. Check that the
new version is live with `snap info screen-edge-light` or on the store dashboard.
