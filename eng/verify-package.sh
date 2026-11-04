#!/usr/bin/env bash
# Packs Nast.Html2Pdf, inspects the package, then installs it into an independent consumer application for each
# supported runtime and runs it, both normally and (where the platform allows) without any network access.
#
# Usage: eng/verify-package.sh [--offline required|auto|skip] [--frameworks "net8.0 net10.0"]
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
offline="auto"
frameworks="net8.0 net10.0"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --offline) offline="$2"; shift 2 ;;
    --frameworks) frameworks="$2"; shift 2 ;;
    *) echo "unknown argument $1" >&2; exit 2 ;;
  esac
done

artifacts="$root/artifacts"
feed="$artifacts/packages"
rm -rf "$feed" "$artifacts/consumer"
mkdir -p "$feed"

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/src/Nast.Html2Pdf/Nast.Html2Pdf.csproj" | head -1)"
echo "==> Packing Nast.Html2Pdf $version"
dotnet pack "$root/src/Nast.Html2Pdf/Nast.Html2Pdf.csproj" -c Release -o "$feed" -p:ContinuousIntegrationBuild=true --nologo

package="$feed/Nast.Html2Pdf.$version.nupkg"
echo "==> Inspecting $package"
python_bin="$(command -v python3 || command -v python)"
list_zip() { "$python_bin" -c 'import sys, zipfile; print("\n".join(zipfile.ZipFile(sys.argv[1]).namelist()))' "$1" | tr -d '\r'; }
read_zip() { "$python_bin" -c 'import sys, zipfile; sys.stdout.write(zipfile.ZipFile(sys.argv[1]).read(sys.argv[2]).decode())' "$1" "$2" | tr -d '\r'; }
contents="$(list_zip "$package")"
for required in "lib/net8.0/Nast.Html2Pdf.dll" "lib/net8.0/Nast.Html2Pdf.xml" "README.md" "LICENSE" "Nast.Html2Pdf.nuspec"; do
  grep -qx "$required" <<<"$contents" || { echo "missing from package: $required" >&2; exit 1; }
done
# Strict allowlist: no browser binaries, symbols or stray build output may ship in the package.
unexpected="$(grep -Ev '^(lib/net8\.0/Nast\.Html2Pdf\.(dll|xml)|README\.md|LICENSE|Nast\.Html2Pdf\.nuspec|\[Content_Types\]\.xml|_rels/\.rels|package/services/metadata/core-properties/[0-9a-z]+\.psmdcp)$' <<<"$contents" || true)"
if [[ -n "$unexpected" ]]; then
  echo "unexpected files in package:" >&2; echo "$unexpected" >&2; exit 1
fi
nuspec="$(read_zip "$package" Nast.Html2Pdf.nuspec)"
grep -q "<version>$version</version>" <<<"$nuspec" || { echo "nuspec version mismatch" >&2; exit 1; }
grep -q 'repository type="git" url="https://github.com/NastMz/Html2Pdf' <<<"$nuspec" || { echo "nuspec repository metadata missing" >&2; exit 1; }
commit="$(git -C "$root" rev-parse HEAD 2>/dev/null || true)"
if [[ -n "$commit" ]] && ! grep -q "commit=\"$commit\"" <<<"$nuspec"; then
  echo "nuspec does not record the source revision $commit" >&2; exit 1
fi
echo "    package files: $(wc -l <<<"$contents"), source revision: ${commit:-unknown}"
echo "    dependencies:"; grep -o '<dependency id="[^"]*" version="[^"]*"' <<<"$nuspec" | sed 's/<dependency /      /'

# Isolated package cache so the consumer can only resolve the package that was just packed.
export NUGET_PACKAGES="$artifacts/consumer/nuget"
consumer_src="$root/tests/PackageConsumer"
for tfm in $frameworks; do
  out="$artifacts/consumer/$tfm"
  echo "==> Building consumer for $tfm against the packed package"
  dotnet build "$consumer_src/PackageConsumer.csproj" -c Release -f "$tfm" -o "$out" -p:Html2PdfVersion="$version" \
    --source "$feed" --source https://api.nuget.org/v3/index.json --nologo -v quiet
  echo "==> Running consumer ($tfm)"
  dotnet "$out/PackageConsumer.dll"

  if [[ "$offline" != "skip" && "$(uname -s)" == "Linux" ]]; then
    if command -v unshare >/dev/null && unshare -rn true 2>/dev/null || { [[ $EUID -eq 0 ]] && unshare -n true 2>/dev/null; }; then
      echo "==> Running consumer ($tfm) with no network access (network namespace with loopback only)"
      flags="-rn"; [[ $EUID -eq 0 ]] && flags="-n"
      unshare $flags sh -c "ip link set lo up 2>/dev/null || python3 '$root/eng/loopback-up.py'; \
        if curl -sS -m 3 https://api.nuget.org/v3/index.json -o /dev/null 2>/dev/null; then echo 'network unexpectedly reachable' >&2; exit 1; fi; \
        exec dotnet '$out/PackageConsumer.dll'"
    elif [[ "$offline" == "required" ]]; then
      echo "offline verification required but network namespaces are unavailable" >&2; exit 1
    else
      echo "    (offline run skipped: network namespaces unavailable on this platform)"
    fi
  elif [[ "$offline" == "required" ]]; then
    echo "offline verification requires Linux network namespaces" >&2; exit 1
  fi
done
echo "==> Package verification succeeded for $version"
