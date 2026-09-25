using TMPro;
using UnityEngine;

/// <summary>
/// The results panel's account of where the score came from: the four parts of the drill
/// score, what each was measured on, and the badge.
///
/// Counts only, never names: it says how many essentials were packed and how much junk came
/// along, but not which items. Working out what was missing is left to the student.
/// </summary>
public class ScoreBreakdownView : MonoBehaviour
{
    [Header("Parts: points")]
    [SerializeField] private TextMeshProUGUI packingPoints;
    [SerializeField] private TextMeshProUGUI quizPoints;
    [SerializeField] private TextMeshProUGUI taskPoints;
    [SerializeField] private TextMeshProUGUI timePoints;

    [Header("Parts: what each was measured on")]
    [SerializeField] private TextMeshProUGUI packingDetail;
    [SerializeField] private TextMeshProUGUI quizDetail;
    [SerializeField] private TextMeshProUGUI taskDetail;
    [SerializeField] private TextMeshProUGUI timeDetail;

    [Header("Summary")]
    [SerializeField] private TextMeshProUGUI badgeText;

    public void Show(DrillScore.Result drill, float remainingTime)
    {
        int[] points = DrillScore.WholePoints(drill);

        SetText(packingPoints, $"{points[0]}/{DrillScore.PACKING_WEIGHT:0}");
        SetText(quizPoints, $"{points[1]}/{DrillScore.QUIZ_WEIGHT:0}");
        SetText(taskPoints, $"{points[2]}/{DrillScore.TASK_WEIGHT:0}");
        SetText(timePoints, $"{points[3]}/{DrillScore.TIME_WEIGHT:0}");

        string packing = $"{drill.EssentialsPacked}/{drill.EssentialsTarget} essentials";
        if (drill.JunkCount > 0)
            packing += $", {drill.JunkCount} junk (-{drill.Deductions}%)";
        SetText(packingDetail, packing);

        SetText(quizDetail, $"{drill.CorrectAnswers}/{drill.QuestionsAsked} correct");
        SetText(taskDetail, $"{drill.TasksCompleted}/{drill.QuestionsAsked} finished");

        // A zero here is otherwise a mystery: the clock may have been fine, but the bonus is
        // only paid out once the bag and the quiz are good enough
        SetText(timeDetail, drill.SpeedGatePassed
            ? $"{FormatClock(remainingTime)} left"
            : $"needs {DrillScore.SPEED_GATE:0}% packing & quiz");

        SetText(badgeText, (drill.Badge ?? "").ToUpperInvariant());
    }

    private static string FormatClock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{whole / 60}:{whole % 60:00}";
    }

    private static void SetText(TextMeshProUGUI label, string text)
    {
        if (label != null)
            label.text = text;
    }
}
