using UnityEngine;
using Firebase.Firestore;
using Firebase.Auth;
using Firebase.Extensions;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;
using TMPro;

public class StudentLoginManager : MonoBehaviour
{
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private TextMeshProUGUI errorText;
    [SerializeField] private UnityEngine.UI.Button loginButton;
    [SerializeField] private UnityEngine.UI.Toggle termsAndConditionsCheckbox;

    private FirebaseFirestore db;
    private FirebaseAuth auth;
    private bool isLoggingIn = false;

    private void Start()
    {
        db = FirebaseFirestore.DefaultInstance;
        auth = FirebaseAuth.DefaultInstance;
        
        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginButtonClicked);

        // Check if already logged in AND Firebase Auth session still exists
        if (PlayerPrefs.HasKey("StudentId") && auth.CurrentUser != null)
        {
            SceneNavigationManager.Instance.GoToMainScene();
        }
        else if (PlayerPrefs.HasKey("StudentId"))
        {
            // PlayerPrefs exists but Firebase session expired - clear and show login
            PlayerPrefs.DeleteKey("StudentId");
            PlayerPrefs.DeleteKey("StudentName");
            PlayerPrefs.DeleteKey("StudentUsername");
            PlayerPrefs.DeleteKey("TeacherId");
            PlayerPrefs.DeleteKey("StudentSection");
            PlayerPrefs.Save();
        }
    }

    private async void OnLoginButtonClicked()
    {
        if (isLoggingIn) return;

        string username = usernameInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            ShowError("Please enter username and password");
            return;
        }

        await AuthenticateStudent(username, password);
    }

    private async System.Threading.Tasks.Task AuthenticateStudent(string username, string password)
    {
        isLoggingIn = true;
        if (loginButton != null) loginButton.interactable = false;
        ShowError("");

        try
        {
            // Clear guest flag when logging in as student
            PlayerPrefs.DeleteKey("IsGuest");

            // Convert username to email format (username@readysetbag.local)
            string studentEmail = $"{username}@readysetbag.local";

            // Sign in with Firebase Auth using the student's credentials
            AuthResult authResult = await auth.SignInWithEmailAndPasswordAsync(studentEmail, password);
            Firebase.Auth.FirebaseUser user = authResult.User;

            // Look up this student's profile. It is found by Auth uid rather than username: the
            // Firestore rules only let a student read their own record, and a query has to
            // be limited to that record for the rules to allow it.
            QuerySnapshot snapshot = null;
            try
            {
                Query query = db.Collection("students").WhereEqualTo("authUid", user.UserId).Limit(1);
                snapshot = await query.GetSnapshotAsync();
            }
            catch (System.Exception firebaseQueryEx)
            {
                ShowError("Database query failed. Please try again.");
                isLoggingIn = false;
                if (loginButton != null) loginButton.interactable = true;
                return;
            }

            if (snapshot.Count == 0)
            {
                ShowError("Account not found in database!");
                auth.SignOut();
                isLoggingIn = false;
                if (loginButton != null) loginButton.interactable = true;
                return;
            }

            // Check terms and conditions after credentials are verified
            if (!termsAndConditionsCheckbox.isOn)
            {
                ShowError("Please accept terms and conditions first");
                auth.SignOut();
                isLoggingIn = false;
                if (loginButton != null) loginButton.interactable = true;
                return;
            }

            // Authentication successful - save student info and load MainScene
            DocumentSnapshot studentDoc = snapshot.Documents.First();
            string studentId = studentDoc.Id;
            string displayName = GetStringOrEmpty(studentDoc, "displayName");
            string teacherId = GetStringOrEmpty(studentDoc, "teacherId");
            string section = GetStringOrEmpty(studentDoc, "section");

            // Save to PlayerPrefs for persistence
            PlayerPrefs.SetString("StudentId", studentId);
            PlayerPrefs.SetString("StudentName", displayName);
            PlayerPrefs.SetString("StudentUsername", username);
            PlayerPrefs.SetString("TeacherId", teacherId);
            PlayerPrefs.SetString("StudentSection", section);
            PlayerPrefs.Save();

            // Load MainScene scene
            SceneNavigationManager.Instance.GoToMainScene();
        }
        catch (Firebase.FirebaseException ex)
        {
            string errorMsg = ex.Message.ToLower();
            
            // Error code 1 from Firebase can be wrong password or wrong username
            if (ex.ErrorCode == 1)
            {
                ShowError("Invalid username or password!");
            }
            else if (errorMsg.Contains("permission") || errorMsg.Contains("permission denied"))
            {
                ShowError("Permission error. Please contact your teacher.");
            }
            else if (errorMsg.Contains("user disabled"))
            {
                ShowError("This account has been disabled.");
            }
            else if (errorMsg.Contains("too many"))
            {
                ShowError("Too many failed login attempts. Please try again later.");
            }
            else
            {
                ShowError("Login failed: " + ex.Message);
            }
            isLoggingIn = false;
            if (loginButton != null) loginButton.interactable = true;
        }
        catch (System.Exception ex)
        {
            ShowError("Login error: " + ex.Message);
            isLoggingIn = false;
            if (loginButton != null) loginButton.interactable = true;
        }
    }

    private static string GetStringOrEmpty(DocumentSnapshot doc, string field)
    {
        string value;
        return doc.TryGetValue(field, out value) && value != null ? value : "";
    }

    private void ShowError(string message)
    {
        if (errorText != null)
            errorText.text = message;
    }

    // Static method to check if student is logged in
    public static bool IsLoggedIn()
    {
        return PlayerPrefs.HasKey("StudentId");
    }

    // Static method to logout
    public static void Logout()
    {
        // Sign out from Firebase Auth
        FirebaseAuth.DefaultInstance.SignOut();

        PlayerPrefs.DeleteKey("StudentId");
        PlayerPrefs.DeleteKey("StudentName");
        PlayerPrefs.DeleteKey("StudentUsername");
        PlayerPrefs.DeleteKey("TeacherId");
        PlayerPrefs.DeleteKey("StudentSection");
        PlayerPrefs.Save();
    }

    // Static methods to get student info
    public static string GetStudentId() => PlayerPrefs.GetString("StudentId", "");
    public static string GetStudentName() => PlayerPrefs.GetString("StudentName", "");
    public static string GetStudentUsername() => PlayerPrefs.GetString("StudentUsername", "");
    public static string GetTeacherId() => PlayerPrefs.GetString("TeacherId", "");
}