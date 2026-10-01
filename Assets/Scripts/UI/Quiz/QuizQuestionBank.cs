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
