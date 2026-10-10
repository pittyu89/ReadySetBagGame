using UnityEngine;

/// <summary>
/// Question data structure for earthquake preparedness quiz.
/// Stores question text, multiple correct answer item names, and the lines shown after
/// the verdict.
/// </summary>
[System.Serializable]
public struct QuestionData
{
    [TextArea(2, 6)]
    public string questionText;

    [Tooltip("Item names (SupplyItem.ItemName) that answer the question. Any one of them is correct.")]
    public string[] correctAnswerItemNames;

    // Shown under CORRECT / NICE TRY, to say why. Optional: a question that leaves these
    // empty just shows the verdict on its own, the way every question used to.
    [TextArea(2, 6)]
    public string correctFeedback;
    [TextArea(2, 6)]
    public string incorrectFeedback;

    // Voice-over read out alongside each line above. Optional: a line with no clip is
    // just typed out in silence.
    public VoiceLine questionVoice;
    public VoiceLine correctFeedbackVoice;
    public VoiceLine incorrectFeedbackVoice;
}

/// <summary>
/// A recorded line and when each part of its text is spoken, so the typewriter can keep
/// pace with the voice instead of running at its own speed.
/// </summary>
[System.Serializable]
public struct VoiceLine
{
    public AudioClip clip;

    [Tooltip("Sync keys: x = characters shown, y = seconds into the clip. Worked out from " +
             "the recording's pauses; in order of time. Empty types the line at the normal " +
             "speed over the length of the clip. If the text is edited, re-record the line " +
             "and regenerate these — the keys are scaled to the new length meanwhile.")]
    public Vector2[] sync;

    public bool HasClip => clip != null;

    /// <summary>
    /// How many of <paramref name="totalChars"/> characters should be showing
    /// <paramref name="time"/> seconds into the clip.
    /// </summary>
    public int CharsAt(float time, int totalChars)
    {
        if (clip == null || totalChars <= 0)
            return totalChars;

        if (sync == null || sync.Length < 2)
            return Mathf.Clamp(Mathf.FloorToInt(time / Mathf.Max(0.01f, clip.length) * totalChars), 0, totalChars);

        // The keys were made for the text as it was recorded; scale them if it has changed
        float scale = totalChars / Mathf.Max(1f, sync[sync.Length - 1].x);

        if (time <= sync[0].y)
            return Mathf.RoundToInt(sync[0].x * scale);

        for (int i = 1; i < sync.Length; i++)
        {
            if (time > sync[i].y)
                continue;

            Vector2 a = sync[i - 1];
            Vector2 b = sync[i];
            float t = b.y > a.y ? (time - a.y) / (b.y - a.y) : 1f;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.Lerp(a.x, b.x, t) * scale), 0, totalChars);
        }

        return totalChars;
    }
}

/// <summary>
/// The earthquake preparedness question pool the quiz draws from. Each round takes a random
/// selection (see QuizManager.RandomizeQuestions), so the order does not matter for play -
/// but a saved drill remembers its questions by their place in this list, so add new
/// questions at the end and don't reorder or remove the ones already here mid-term, or a drill
/// resumed from before the change would come back with different questions.
///
/// Add to the list and the round grows to match: the length drives how many questions are
/// asked, the score denominator, and the debug picker's dropdown.
/// </summary>
[CreateAssetMenu(fileName = "QuizQuestionBank", menuName = "Ready Set Bag/Quiz Question Bank")]
public class QuizQuestionBank : ScriptableObject
{
    [SerializeField] private QuestionData[] questions = new QuestionData[0];

    public QuestionData[] Questions => questions;
}
