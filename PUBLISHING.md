# How to Update and Publish Screen Edge Light

## 1. Make Your Changes
Edit the code, fix bugs, or add features as normal. You can test your changes locally using:
```bash
dotnet run --project UbuntuEdgeLight.Linux/UbuntuEdgeLight.Linux.csproj
```

## 2. Update the Version
Open `snap/snapcraft.yaml` and increase the `version` number.
```yaml
name: screen-edge-light
version: '1.1'  <-- Change this (e.g., 1.0 -> 1.1)
```

## 3. Rebuild the Package
Run the build command in the root of your project:
```bash
snapcraft pack
```
This will create a new file, for example: `screen-edge-light_1.1_amd64.snap`.

## 4. Test Locally (Optional but Recommended)
Install the new file to make sure it works before releasing:
```bash
sudo snap install screen-edge-light_1.1_amd64.snap --dangerous
```

## 5. Upload to the Store
Push the new version to the stable channel:
```bash
snapcraft upload --release=stable screen-edge-light_1.1_amd64.snap
```
*(Make sure to replace the filename with the actual one created in Step 3)*

## 6. Verification
Your users will automatically receive the update in the background within a few hours. You can verify the new version is live on the store dashboard at [snapcraft.io](https://snapcraft.io).
