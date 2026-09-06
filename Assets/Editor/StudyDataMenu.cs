using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only menu for managing the study's recorded CSV data (see
/// SessionController's Write*Csv methods and StudyDataDir/StudyDataSummary/
/// ClearAllStudyData). Lives under "Tools/Study Data" in Unity's menu bar. Not part
/// of the in-VR runtime UI, and stripped from player builds automatically since this
/// whole file sits in an Editor/ folder.
/// </summary>
public static class StudyDataMenu
{
    private const string ClearMenuPath = "Tools/Study Data/Clear Study Data...";
    private const string SummaryMenuPath = "Tools/Study Data/Show Summary";
    private const string OpenFolderMenuPath = "Tools/Study Data/Open Data Folder";

    [MenuItem(ClearMenuPath, priority = 1)]
    private static void ClearStudyData()
    {
        string summary = SessionController.StudyDataSummary();
        bool confirmed = EditorUtility.DisplayDialog(
            "Clear Study Data?",
            "This will permanently delete all recorded CSV data:\n\n" + summary +
            "\n\nThis cannot be undone. Use this to wipe out test/pilot runs before " +
            "real data collection.",
            "Delete Everything",
            "Cancel");
        if (!confirmed) return;

        string result = SessionController.ClearAllStudyData();
        EditorUtility.DisplayDialog("Study Data Cleared", result, "OK");
    }

    // Greys the menu item out when there's nothing to clear.
    [MenuItem(ClearMenuPath, true)]
    private static bool ValidateClearStudyData() => SessionController.HasStudyData();

    [MenuItem(SummaryMenuPath, priority = 20)]
    private static void ShowSummary()
    {
        EditorUtility.DisplayDialog("Study Data Summary", SessionController.StudyDataSummary(), "OK");
    }

    [MenuItem(OpenFolderMenuPath, priority = 21)]
    private static void OpenDataFolder()
    {
        string dir = SessionController.StudyDataDir();
        Directory.CreateDirectory(dir);
        EditorUtility.RevealInFinder(dir);
    }
}
