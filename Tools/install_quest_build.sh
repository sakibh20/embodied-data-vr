#!/bin/bash
# macOS: installs Builds/Quest/EmbodiedDataViz.apk on the connected Quest.
# -r replaces the app but KEEPS its data (never uninstall: that deletes StudyData).
# Run from Terminal:  bash Tools/install_quest_build.sh
cd "$(dirname "$0")" || exit 1
source ./_find_adb.sh
APK="../Builds/Quest/EmbodiedDataViz.apk"
[ -f "$APK" ] || { echo "No build at $APK -- build the Android target in Unity first."; exit 1; }
"$ADB" -s "$QUEST" install -r "$APK" && echo "Installed. In the headset: Library > Unknown Sources > Thesis"
