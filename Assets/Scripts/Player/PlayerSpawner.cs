using UnityEngine;
using Cinemachine;

public class PlayerSpawner : MonoBehaviour
{
    [SerializeField] private GameObject femaleCharacterPrefab;
    [SerializeField] private GameObject maleCharacterPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private GameObject existingPlayer;
    [SerializeField] private CinemachineVirtualCamera cinemachineCamera;
    [SerializeField] private AudioClip backgroundMusic;

    private const string SELECTED_CHARACTER_SUFFIX = "_SelectedCharacter";

    void Start()
    {
        // Start background music
        if (backgroundMusic != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayMusic(backgroundMusic, loop: true);
        }

        // Load the selected character for the current user
        string userKey = GetUserKey();
        string selectedCharacter = PlayerPrefs.GetString(userKey, "Female");
        
        // Determine which prefab to use
        GameObject prefabToSpawn = selectedCharacter == "Male" ? maleCharacterPrefab : femaleCharacterPrefab;
        
        if (prefabToSpawn == null)
        {
            return;
        }

        // Get the spawn position (use existingPlayer position or spawnPoint)
        Vector3 spawnPosition = existingPlayer != null ? existingPlayer.transform.position : 
                                (spawnPoint != null ? spawnPoint.position : Vector3.zero);

        // Instantiate the correct prefab
        GameObject spawnedCharacter = Instantiate(prefabToSpawn, spawnPosition, Quaternion.identity);

        // Destroy the existing player if it exists
        if (existingPlayer != null)
        {
            Destroy(existingPlayer);
        }

        // Follow and look at the spawned character. LookAt is also what the camera's wall
        // collider keeps line of sight to, so without it the camera passes through walls.
        if (cinemachineCamera != null)
        {
            cinemachineCamera.Follow = spawnedCharacter.transform;
            cinemachineCamera.LookAt = spawnedCharacter.transform;
        }
    }

    private string GetUserKey()
    {
        bool isGuest = PlayerPrefs.GetString("IsGuest", "false") == "true";
        string userName = isGuest ? "Guest" : PlayerPrefs.GetString("StudentName", "User");
        return userName + SELECTED_CHARACTER_SUFFIX;
    }
}

