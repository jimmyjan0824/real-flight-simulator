#!/usr/bin/env bash
# Build the Unity project into an Xcode project (build/iOS) from the command line.
#
# Usage:
#   ./scripts/build_ios.sh            # build, then open the Xcode project
#   ./scripts/build_ios.sh --no-open  # build only
#
# Environment overrides:
#   UNITY_PATH   full path to the Unity binary (default: newest 6000.* under Unity Hub)
#   BUNDLE_ID    bundle identifier (default set in Assets/Editor/BuildScript.cs)
#   IOS_TEAM_ID  Apple Team ID (optional; otherwise choose the team in Xcode)
#   BUILD_PATH   output folder (default: build/iOS)
#
# NOTE: close the project in the Unity Editor first; Unity refuses to open
# the same project twice.
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LOG_DIR="$PROJECT_DIR/build"
LOG_FILE="$LOG_DIR/unity_build_ios.log"
export BUILD_PATH="${BUILD_PATH:-build/iOS}"
OPEN_XCODE=1
[[ "${1:-}" == "--no-open" ]] && OPEN_XCODE=0

if [[ -z "${UNITY_PATH:-}" ]]; then
  HUB_DIR="/Applications/Unity/Hub/Editor"
  LATEST="$(ls -1 "$HUB_DIR" 2>/dev/null | grep '^6000\.' | sort -V | tail -n 1 || true)"
  if [[ -z "$LATEST" ]]; then
    echo "ERROR: no Unity 6 (6000.x) editor found in $HUB_DIR." >&2
    echo "       Install it via Unity Hub, or set UNITY_PATH." >&2
    exit 1
  fi
  UNITY_PATH="$HUB_DIR/$LATEST/Unity.app/Contents/MacOS/Unity"
fi

if [[ ! -x "$UNITY_PATH" ]]; then
  echo "ERROR: Unity binary not found or not executable: $UNITY_PATH" >&2
  exit 1
fi

# .../Editor/<version>/Unity.app/Contents/MacOS/Unity -> .../Editor/<version>
EDITOR_ROOT="$(cd "$(dirname "$UNITY_PATH")/../../.." && pwd)"
if [[ ! -d "$EDITOR_ROOT/PlaybackEngines/iOSSupport" ]]; then
  echo "WARNING: iOS Build Support not found at $EDITOR_ROOT/PlaybackEngines/iOSSupport" >&2
  echo "         Unity Hub > Installs > (gear) > Add modules > iOS Build Support." >&2
fi

mkdir -p "$LOG_DIR"
echo "Unity:   $UNITY_PATH"
echo "Project: $PROJECT_DIR"
echo "Log:     $LOG_FILE"
echo "Building... (first run imports the project and can take several minutes)"

set +e
"$UNITY_PATH" \
  -batchmode -nographics -quit \
  -projectPath "$PROJECT_DIR" \
  -buildTarget iOS \
  -executeMethod BuildScript.BuildIOS \
  -logFile "$LOG_FILE"
STATUS=$?
set -e

if [[ $STATUS -ne 0 ]]; then
  echo "BUILD FAILED (exit $STATUS). Last 40 lines of the log:" >&2
  tail -n 40 "$LOG_FILE" >&2 || true
  exit $STATUS
fi

XCODEPROJ="$PROJECT_DIR/$BUILD_PATH/Unity-iPhone.xcodeproj"
echo "BUILD OK: $XCODEPROJ"
if [[ $OPEN_XCODE -eq 1 && -d "$XCODEPROJ" ]]; then
  open "$XCODEPROJ"
fi
