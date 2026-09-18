using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles the finish door interaction.
/// When player (with bag) enters the door trigger, shows a prompt.
/// Player can choose to proceed to quiz or go back.
/// </summary>
public class FinishDoorHandler : MonoBehaviour
{
    [SerializeField] private GameObject finishPromptPanel;
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject quizPanel;
    
    [SerializeField] private Button yesButton;
    [SerializeField] private Button noButton;
    [SerializeField] private float detectionDistance = 3f;

    private bool hasTriggered = false;
    private Transform playerTransform;
    private InventoryPanel inventoryPanelHandler;
    private QuizManager quizHandler;
    private GameTimer timerScript;
    private bool isPlayerInRange = false;

    void Start()
    {
        // Ensure panels are hidden initially
        if (finishPromptPanel != null)
            finishPromptPanel.SetActive(false);

        if (quizPanel != null)
            quizPanel.SetActive(false);

        // Setup button listeners
        if (yesButton != null)
            yesButton.onClick.AddListener(OnYesClicked);

        if (noButton != null)
            noButton.onClick.AddListener(OnNoClicked);
        
        // Find the player
        GameObject playerObj = GameObject.FindWithTag("Player");
        
        if (playerObj != null)
            playerTransform = playerObj.transform;

        // Find InventoryPanel
        inventoryPanelHandler = FindObjectOfType<InventoryPanel>();

        // Find Timer script
        timerScript = FindObjectOfType<GameTimer>();

        // Subscribe to timer event
        GameTimer.OnTimeUp += ShowQuizAtTimeUp;

        // Don't find QuizManager here - it may be inactive
        // We'll find it when needed
    }

    private int frameCount = 0;

    void LateUpdate()
    {
        // Re-find player if lost (in case they get respawned)
        if (playerTransform == null)
        {
            GameObject playerObj = GameObject.FindWithTag("Player");
            if (playerObj != null)
                playerTransform = playerObj.transform;
            else
                return;
        }

        if (!GoBagPickup.IsBagPickedUp())
        {
            isPlayerInRange = false;
            return;
        }

        float distance = Vector3.Distance(transform.position, playerTransform.position);

        // Player entered range
        if (distance <= detectionDistance && !isPlayerInRange)
        {
            isPlayerInRange = true;

            // Only trigger the prompt if it hasn't been triggered yet
            if (!hasTriggered)
            {
                hasTriggered = true;
                ShowFinishPrompt();
            }
        }
        // Player left range
        else if (distance > detectionDistance && isPlayerInRange)
        {
            isPlayerInRange = false;
            // Reset trigger so it can fire again on next approach
            hasTriggered = false;
            // Hide prompt if it's still showing
            if (finishPromptPanel != null && finishPromptPanel.activeSelf)
                finishPromptPanel.SetActive(false);
        }
    }

    private void ShowFinishPrompt()
    {
        if (finishPromptPanel != null)
            finishPromptPanel.SetActive(true);
    }

    public void OnYesClicked()
    {
        // Pause the timer
        if (timerScript != null)
        {
            timerScript.PauseTimer();
        }

        ShowQuizAtTimeUp();
    }

    private void ShowQuizAtTimeUp()
    {
        // Pause the timer first to prevent any audio updates
        if (timerScript != null)
        {
            timerScript.PauseTimer();
            timerScript.StopAudio();
        }

        // Hide prompt
        if (finishPromptPanel != null)
            finishPromptPanel.SetActive(false);

        // Open inventory in half-screen mode (left side) so quiz panel can be on right
        if (inventoryPanelHandler != null)
        {
            inventoryPanelHandler.OpenInventoryHalfScreen();
            inventoryPanelHandler.HideCloseButton();
        }

        // Show quiz panel
        if (quizPanel != null)
            quizPanel.SetActive(true);

        // Find QuizManager if not already cached (may be on inactive object initially)
        if (quizHandler == null)
        {
            quizHandler = FindObjectOfType<QuizManager>(true); // includeInactive = true
        }

        // Open the quiz
        if (quizHandler != null)
        {
            quizHandler.OpenQuiz();
        }
    }

    private void OnNoClicked()
    {
        // Hide prompt only
        if (finishPromptPanel != null)
            finishPromptPanel.SetActive(false);
        
        // Don't reset hasTriggered here - let the distance check handle it
        // When player leaves range, it will automatically reset
    }

    private void OnDestroy()
    {
        // Unsubscribe from timer event to prevent memory leaks
        GameTimer.OnTimeUp -= ShowQuizAtTimeUp;
    }
}
