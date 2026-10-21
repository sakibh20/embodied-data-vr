# Shared by the macOS scripts: finds Unity's adb (or one on PATH) and the Quest's serial.
UNITY_VERSION="6000.3.10f1"
ADB=""
for c in \
  "/Applications/Unity/Hub/Editor/$UNITY_VERSION/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb" \
  "/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb" \
  "$HOME/Library/Android/sdk/platform-tools/adb"; do
  [ -x "$c" ] && { ADB="$c"; break; }
done
[ -z "$ADB" ] && command -v adb >/dev/null 2>&1 && ADB="$(command -v adb)"
if [ -z "$ADB" ]; then
  echo "adb not found. Install Unity $UNITY_VERSION with Android Build Support (+ Android SDK & NDK Tools)."
  exit 1
fi

"$ADB" start-server >/dev/null 2>&1
# Pick the Quest by model name, so a phone plugged in at the same time is never used.
QUEST="$("$ADB" devices -l | awk '/model:Quest/ && $2=="device" {print $1; exit}')"
if [ -z "$QUEST" ]; then
  echo "No Quest found. Connected devices:"
  "$ADB" devices -l
  echo
  echo "Check: headset on and awake, USB cable is a data cable, 'Allow USB debugging' accepted"
  echo "in the headset ('unauthorized' above = not accepted yet), Developer Mode on."
  exit 1
fi
echo "Using Quest $QUEST"
