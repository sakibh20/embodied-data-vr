#!/bin/bash
# macOS: copies the study data off the Quest into QuestData/<date-time>/ (git-ignored).
# Run from Terminal:  bash Tools/pull_quest_data.sh
cd "$(dirname "$0")" || exit 1
source ./_find_adb.sh
DEST="../QuestData/$(date +%Y%m%d-%H%M%S)"
mkdir -p "$DEST"
if "$ADB" -s "$QUEST" pull /sdcard/Android/data/com.sakibhasan.embodieddataviz/files/StudyData "$DEST"; then
  echo "Copied to $(cd "$DEST" && pwd)"
else
  echo "FAILED to copy. Is the app installed and has a session been run?"
fi
