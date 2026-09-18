using UnityEngine;
using Firebase.Firestore;
using Firebase.Extensions;
using System.Linq;

public class GameSessionMonitor : MonoBehaviour
{
    private FirebaseFirestore db;
    private ListenerRegistration sessionListener;
    private string currentSessionCode;
    private bool isMonitoring = false;

    private void Start()
    {
        // Only monitor in teacher session mode
        currentSessionCode = PlayerPrefs.GetString("SessionCode", "");
        
        if (!string.IsNullOrEmpty(currentSessionCode))
        {
            StartMonitoring();
        }
    }

    private void StartMonitoring()
    {
        if (isMonitoring)
            return;

        db = FirebaseFirestore.DefaultInstance;
        isMonitoring = true;

        try
        {
            // Subscribe to Firestore listener for the session
            sessionListener = db.Collection("sessions")
                .WhereEqualTo("sessionCode", currentSessionCode)
                .Limit(1)
                .Listen(snapshot =>
                {
                    try
                    {
                        if (snapshot.Count > 0)
                        {
                            DocumentSnapshot doc = snapshot.Documents.First();
                            string status = doc.GetValue<string>("status");
                            
                            if (status == "ended")
                            {
                                StopMonitoring();
                                
                                // Resume time before redirecting
                                Time.timeScale = 1f;
                                
                                // Stop music
                                SoundManager.Instance.StopMusic();
                                
                                // Clear SessionCode
                                PlayerPrefs.DeleteKey("SessionCode");
                                PlayerPrefs.Save();
                                
                                // Redirect to MainScene
                                SceneNavigationManager.Instance.GoToMainScene();
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                    }
                });
            }
        catch (System.Exception e)
        {
        }
    }

    private void StopMonitoring()
    {
        if (sessionListener != null)
        {
            sessionListener.Stop();
            sessionListener = null;
        }
        isMonitoring = false;
    }

    private void OnDestroy()
    {
        StopMonitoring();
    }
}
