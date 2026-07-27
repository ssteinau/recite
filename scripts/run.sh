#!/usr/bin/env bash
# Launch Recite. On NixOS the prebuilt Skia/X11 native libs can't find their
# transitive system deps on the default loader path, so we discover them in the
# nix store and assemble LD_LIBRARY_PATH for this run only (no system changes).
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${1:-Debug}"
dotnet build src/Recite.App/Recite.App.csproj -c "$CONFIG" -v q --nologo

NATIVE="$PWD/src/Recite.App/bin/$CONFIG/net10.0/runtimes/linux-x64/native"

if [ -d /nix/store ]; then
  libs="libICE.so.6 libSM.so.6 libX11.so.6 libXext.so.6 libXrandr.so.2 \
        libXi.so.6 libXcursor.so.1 libXrender.so.1 libXfixes.so.3 \
        libXinerama.so.1 libxcb.so.1 libfontconfig.so.1 libfreetype.so.6 \
        libGL.so.1 libglib-2.0.so.0"
  extra=""
  for l in $libs; do
    p=$(find /nix/store -maxdepth 3 -name "$l" 2>/dev/null | head -1 || true)
    [ -n "$p" ] && extra="$extra:$(dirname "$p")"
  done
  export LD_LIBRARY_PATH="$NATIVE${extra}:${NIX_LD_LIBRARY_PATH:-}:${LD_LIBRARY_PATH:-}"
fi

exec dotnet run --project src/Recite.App/Recite.App.csproj -c "$CONFIG" --no-build
