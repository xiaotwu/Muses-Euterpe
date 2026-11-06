#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 2 || ! $1 =~ ^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z]+([.-][0-9A-Za-z]+)*)?$ ]]; then
    echo 'Usage: bash scripts/package-deb.sh <version, e.g. 0.1.0> <output-directory>' >&2
    exit 2
fi

if [[ $(uname -s) != Linux || $(uname -m) != x86_64 ]] || [[ $(dpkg --print-architecture) != amd64 ]]; then
    echo 'Build the amd64 package on x64 Linux with dpkg and the .NET 10 SDK.' >&2
    exit 2
fi

version=$1
# Debian sorts ~ below the stable release, matching a SemVer prerelease.
deb_version=${version/-/\~}
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)
mkdir -p -- "$2"
output=$(cd -- "$2" && pwd)
work=$(mktemp -d)
pending=''
trap 'rm -rf -- "$work"; if [[ -n "$pending" ]]; then rm -f -- "$pending"; fi' EXIT

package="$work/package"
app="$package/usr/lib/muses"
dotnet publish "$repo/src/Muses.App/Muses.App.csproj" --configuration Release \
    --runtime linux-x64 --self-contained true --artifacts-path "$work/app-build" --output "$app" \
    -p:Version="$version" -p:DebugType=None
# Publish the isolated process independently so it also runs without a system .NET installation.
dotnet publish "$repo/src/Muses.WebHome.Helper/Muses.WebHome.Helper.csproj" --configuration Release \
    --runtime linux-x64 --self-contained true --artifacts-path "$work/helper-build" --output "$work/helper" \
    -p:Version="$version" -p:DebugType=None

# A development publish can copy local playback tools or a development helper into the payload.
rm -rf -- "$app/Helpers"
rm -f -- "$app/yt-dlp" "$app/yt-dlp.exe" "$app/mpv.exe" "$app/d3dcompiler_43.dll"
mv -- "$work/helper" "$app/Helpers"
python3 "$repo/scripts/stage-media-tools.py" linux-x64 "$app/MediaTools"
test -x "$app/Muses"
test -x "$app/Helpers/MusesWebHomeHelper"
test -x "$app/MediaTools/yt-dlp"
test -x "$app/MediaTools/deno"

install -d -m 0755 "$package/DEBIAN" "$package/usr/bin"
ln -s ../lib/muses/Muses "$package/usr/bin/muses"
install -Dm644 "$repo/scripts/deb/muses.desktop" "$package/usr/share/applications/muses.desktop"
install -Dm644 "$repo/src/Muses.App/Assets/Packaging/Square44x44Logo.targetsize-256.png" \
    "$package/usr/share/icons/hicolor/256x256/apps/muses.png"
install -Dm644 "$repo/LICENSE" "$package/usr/share/doc/muses/copyright"

installed_size=$(du -sk --apparent-size "$package/usr" | cut -f1)
cat > "$package/DEBIAN/control" <<EOF
Package: muses
Version: $deb_version
Section: sound
Priority: optional
Architecture: amd64
Maintainer: xiaotwu <44873243+xiaotwu@users.noreply.github.com>
Installed-Size: $installed_size
Depends: ca-certificates, libc6, libgcc-s1, libgssapi-krb5-2, libicu74, libssl3t64, libstdc++6, tzdata, zlib1g, libx11-6, libice6, libsm6, libfontconfig1, libsecret-1-0, mpv
Recommends: libwebkit2gtk-4.1-0, gstreamer1.0-libav, gstreamer1.0-plugins-bad
Homepage: https://github.com/xiaotwu/Muses-Euterpe
Description: Desktop music library and player for YouTube
 Muses organizes and plays your YouTube music library.
 Includes the YouTube audio resolver and its JavaScript runtime.
EOF

asset="$output/Muses-$version-linux-x64.deb"
pending=$(mktemp "$output/.muses-XXXXXXXX.deb")
dpkg-deb --root-owner-group --build "$package" "$pending"
chmod 0644 "$pending"
mv -fT -- "$pending" "$asset"
pending=''
printf 'Wrote %s\n' "$asset"
