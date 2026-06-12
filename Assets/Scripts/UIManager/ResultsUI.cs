using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the Results panel UI — shows final race standings.
/// Attach this to the Results GameObject in the Game scene.
/// 
/// SETUP:
/// 1. Attach to the "Results" GameObject in the Game scene.
/// 2. The script auto-finds the "1st", "2nd", "3rd", "4th" Text children by name.
/// 3. The Results panel starts disabled and is shown when RaceManager broadcasts results.
/// </summary>
public class ResultsUI : MonoBehaviour
{
    [Header("Position Text Names (auto-found in children)")]
    public string firstTextName = "1st";
    public string secondTextName = "2nd";
    public string thirdTextName = "3rd";
    public string fourthTextName = "4th";

    private Text firstText;
    private Text secondText;
    private Text thirdText;
    private Text fourthText;

    private GameObject panel;

    private void Awake()
    {
        // Find the Panel child
        Transform panelTransform = transform.Find("Panel");
        if (panelTransform != null)
        {
            panel = panelTransform.gameObject;
        }
        else
        {
            // If no Panel child, use this gameObject as the panel
            panel = gameObject;
        }

        // Find position text objects in children (including inactive)
        Text[] allTexts = GetComponentsInChildren<Text>(true);
        foreach (Text text in allTexts)
        {
            if (text.gameObject.name == firstTextName) firstText = text;
            else if (text.gameObject.name == secondTextName) secondText = text;
            else if (text.gameObject.name == thirdTextName) thirdText = text;
            else if (text.gameObject.name == fourthTextName) fourthText = text;
        }

        if (firstText == null) Debug.LogWarning($"[ResultsUI] Text '{firstTextName}' not found in children.");
        if (secondText == null) Debug.LogWarning($"[ResultsUI] Text '{secondTextName}' not found in children.");
        if (thirdText == null) Debug.LogWarning($"[ResultsUI] Text '{thirdTextName}' not found in children.");
        if (fourthText == null) Debug.LogWarning($"[ResultsUI] Text '{fourthTextName}' not found in children.");

        // Start hidden
        HideResults();
    }

    /// <summary>
    /// Hides the results panel.
    /// </summary>
    public void HideResults()
    {
        if (panel != null)
            panel.SetActive(false);
    }

    /// <summary>
    /// Populates and shows the results panel with the finish order and usernames.
    /// Called by RaceManager via ClientRpc.
    /// </summary>
    /// <param name="finishOrder">Array of client IDs in finish order. ulong.MaxValue = empty slot.</param>
    /// <param name="usernames">Array of display names matching finishOrder. Empty string = fallback.</param>
    public void ShowResults(ulong[] finishOrder, string[] usernames)
    {
        Debug.Log("[ResultsUI] Showing race results...");

        // Populate each position
        string name1 = (usernames != null && usernames.Length > 0) ? usernames[0] : "";
        string name2 = (usernames != null && usernames.Length > 1) ? usernames[1] : "";
        string name3 = (usernames != null && usernames.Length > 2) ? usernames[2] : "";
        string name4 = (usernames != null && usernames.Length > 3) ? usernames[3] : "";

        SetPositionText(firstText, 1, finishOrder.Length > 0 ? finishOrder[0] : ulong.MaxValue, name1);
        SetPositionText(secondText, 2, finishOrder.Length > 1 ? finishOrder[1] : ulong.MaxValue, name2);
        SetPositionText(thirdText, 3, finishOrder.Length > 2 ? finishOrder[2] : ulong.MaxValue, name3);
        SetPositionText(fourthText, 4, finishOrder.Length > 3 ? finishOrder[3] : ulong.MaxValue, name4);

        // Show the panel
        if (panel != null)
            panel.SetActive(true);

        // Unlock the cursor for UI interaction
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    /// <summary>
    /// Sets a position text field. Shows "X. Username" or "X. ---" if empty slot.
    /// Falls back to "Player N" if username is empty but a player exists.
    /// </summary>
    private void SetPositionText(Text textField, int position, ulong clientId, string username)
    {
        if (textField == null) return;

        if (clientId == ulong.MaxValue)
        {
            // No player in this position
            textField.text = $"{position}. ---";
        }
        else
        {
            // Use the username, fall back to "Player N" if empty
            string displayName = string.IsNullOrEmpty(username)
                ? $"Player {clientId + 1}"
                : username;
            textField.text = $"{position}. {displayName}";
        }
    }
}
