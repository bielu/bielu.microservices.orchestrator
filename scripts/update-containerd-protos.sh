#!/usr/bin/env bash
# -------------------------------------------------------------------
# update-containerd-protos.sh
#
# Downloads containerd gRPC proto files from a tagged GitHub release
# and applies patches required for C# code generation:
#   - Rewrites Go-style import paths to local proto-root-relative paths
#   - Replaces go_package option with csharp_namespace
#   - Renames the Descriptor message to ContentDescriptor to avoid
#     C# naming conflict (member names cannot match enclosing type)
#
# Usage:
#   ./scripts/update-containerd-protos.sh [VERSION]
#
# If VERSION is omitted the value from .containerd-proto-version is used.
#
# Requirements: bash 4+, curl, sed (GNU). Designed for Linux / CI use.
# -------------------------------------------------------------------
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION_FILE="$REPO_ROOT/.containerd-proto-version"
PROTO_DIR="$REPO_ROOT/src/Bielu.Microservices.Orchestrator.Containerd/Proto"

# Resolve version ---------------------------------------------------------
if [[ $# -ge 1 ]]; then
    VERSION="$1"
else
    VERSION="$(tr -d '[:space:]' < "$VERSION_FILE")"
fi

echo "==> Updating containerd protos to ${VERSION}"

BASE_URL="https://raw.githubusercontent.com/containerd/containerd/${VERSION}"

# Map: upstream repo path  →  local path under Proto/
declare -A PROTO_MAP=(
    ["api/types/descriptor.proto"]="containerd/types/descriptor.proto"
    ["api/types/mount.proto"]="containerd/types/mount.proto"
    ["api/types/metrics.proto"]="containerd/types/metrics.proto"
    ["api/types/task/task.proto"]="containerd/types/task/task.proto"
    ["api/services/containers/v1/containers.proto"]="containerd/services/containers/v1/containers.proto"
    ["api/services/tasks/v1/tasks.proto"]="containerd/services/tasks/v1/tasks.proto"
    ["api/services/images/v1/images.proto"]="containerd/services/images/v1/images.proto"
    ["api/services/snapshots/v1/snapshots.proto"]="containerd/services/snapshots/v1/snapshots.proto"
    ["api/services/version/v1/version.proto"]="containerd/services/version/v1/version.proto"
)

# Map: proto package  →  C# namespace
declare -A NS_MAP=(
    ["containerd.types"]="Containerd.Types"
    ["containerd.v1.types"]="Containerd.V1.Types"
    ["containerd.services.containers.v1"]="Containerd.Services.Containers.V1"
    ["containerd.services.tasks.v1"]="Containerd.Services.Tasks.V1"
    ["containerd.services.images.v1"]="Containerd.Services.Images.V1"
    ["containerd.services.snapshots.v1"]="Containerd.Services.Snapshots.V1"
    ["containerd.services.version.v1"]="Containerd.Services.Version.V1"
)

# Download & patch each proto file -----------------------------------------
for upstream in "${!PROTO_MAP[@]}"; do
    local_rel="${PROTO_MAP[$upstream]}"
    dest="$PROTO_DIR/$local_rel"
    url="${BASE_URL}/${upstream}"

    echo "    Downloading ${url}"
    mkdir -p "$(dirname "$dest")"
    curl -fsSL "$url" -o "$dest"

    # 1. Rewrite Go-style import paths to local proto-root-relative paths
    #    e.g. "github.com/containerd/containerd/api/types/mount.proto"
    #      → "containerd/types/mount.proto"
    sed -i 's|github\.com/containerd/containerd/api/|containerd/|g' "$dest"

    # 2. Replace go_package with csharp_namespace
    #    Extract the proto package name to look up the C# namespace.
    pkg=$(grep -oP '(?<=^package\s)[^;]+' "$dest" | tr -d '[:space:]')
    csharp_ns="${NS_MAP[$pkg]:-}"
    if [[ -n "$csharp_ns" ]]; then
        # Remove any existing go_package line
        sed -i '/^option go_package/d' "$dest"
        # Insert csharp_namespace after the package statement
        sed -i "/^package ${pkg};/a\\\\noption csharp_namespace = \"${csharp_ns}\";" "$dest"
    fi

    # 3. Rename Descriptor message to ContentDescriptor to avoid C# naming
    #    conflict where member names cannot match enclosing type.
    if [[ "$local_rel" == "containerd/types/descriptor.proto" ]]; then
        sed -i 's/^message Descriptor {/message ContentDescriptor {/' "$dest"
        # Add a comment explaining the rename
        sed -i '/^message ContentDescriptor {/i\// Renamed to avoid C# naming conflict (member names cannot match enclosing type).' "$dest"
    fi

    # 4. Update all references to the renamed Descriptor type in all protos
    sed -i 's/containerd\.types\.Descriptor/containerd.types.ContentDescriptor/g' "$dest"
done

# Persist the version -------------------------------------------------------
echo "$VERSION" > "$VERSION_FILE"

echo "==> Done. Proto files updated to ${VERSION}"
echo "    Version recorded in ${VERSION_FILE}"
echo ""
echo "    Run 'dotnet build' to regenerate C# stubs."
