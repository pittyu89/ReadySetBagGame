using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Question data structure for earthquake preparedness quiz.
/// Stores question text, multiple correct answer item names, and the lines shown after
/// the verdict.
/// </summary>
[System.Serializable]
public struct QuestionData
{
    public string questionText;
    public string[] correctAnswerItemNames; // Multiple correct answers supported

    // Shown under CORRECT / NICE TRY, to say why. Optional: a question that leaves these
    // empty just shows the verdict on its own, the way every question used to.
    public string correctFeedback;
    public string incorrectFeedback;
}

/// <summary>
/// Drives the quiz as a dialogue sequence.
/// - every question in the list, one at a time, shuffled each run
/// - The question types into the dialogue box, then a single answer box accepts an item
/// - Dropping an item scores it immediately and plays the correct / wrong banner
/// - After the banner the quiz auto-advances to the next question
///
/// A correct item is used up and leaves the go bag. A wrong one stays packed, so the
/// player can still try it on a later question.
/// </summary>
public class QuizManager : MonoBehaviour
{
    [Header("Dialogue Box")]
    [SerializeField] private GameObject dialogueBox;
    [SerializeField] private TextMeshProUGUI questionText;

    [Header("Character")]
    [Tooltip("The teacher asking the questions. Its own Animator plays the talking loop.")]
    [SerializeField] private Image characterPortrait;

    [Header("Answer")]
    [SerializeField] private QuizAnswerBox answerBox;

    [Header("Feedback")]
    [SerializeField] private QuizFeedbackBanner feedbackBanner;
    [Tooltip("Freezes and blurs the whole quiz behind the correct / wrong overlay, so the " +
             "result is the only thing in focus. Sits between the dialogue box and the banner.")]
    [SerializeField] private ScreenBlurBackdrop feedbackScrim;
    [SerializeField] private AudioClip correctSFX;
    [SerializeField] private AudioClip wrongSFX;

    [Header("Question Timer")]
    [Tooltip("Seconds the player gets to answer each question before it is marked wrong. " +
             "Counted from when the question has finished typing, not from when it starts, " +
             "so every question gets the same window whatever its length.")]
    [SerializeField] private float questionTimeLimit = 30f;
    [Tooltip("Countdown readout for the current question. Optional - the limit still " +
             "applies if nothing is assigned.")]
    [SerializeField] private TextMeshProUGUI questionTimerText;
    [Tooltip("Seconds remaining at which the readout switches to the warning colour.")]
    [SerializeField] private float questionTimerWarningThreshold = 5f;
    [SerializeField] private Color questionTimerNormalColor = Color.white;
    [SerializeField] private Color questionTimerWarningColor = new Color(0.90f, 0.30f, 0.28f, 1f);
    [Tooltip("The question's own countdown bar — the same bar the minigames use, so the " +
             "clock reads the same whichever half of the round the player is in. Optional; " +
             "the limit still applies without one.")]
    [SerializeField] private CountdownBar questionTimerBar;

    [Header("Minigame Timer")]
    [Tooltip("Seconds the player gets to finish a minigame. When it runs out the minigame " +
             "is closed where it stands and the quiz moves on, without counting the task " +
             "as done. 0 turns the limit off.")]
    [SerializeField] private float minigameTimeLimit = 30f;
    [Tooltip("Countdown readout for the running minigame. Sits above the minigame panels, " +
             "so it lives on the Canvas rather than inside the quiz. Optional — the bar " +
             "below replaced it, so this is normally left empty.")]
    [SerializeField] private TextMeshProUGUI minigameTimerText;
    [Tooltip("The bar across the top of the screen that drains as the minigame's time runs " +
             "out. Switched on for the minigame and off again after. Optional.\n\n" +
             "Its look and its colours belong to the CountdownBar itself, so the quiz's bar " +
             "and this one cannot drift apart.")]
    [SerializeField] private CountdownBar minigameTimerBar;
    [Tooltip("The COMPLETE! / TIME'S UP! banner shown when a minigame ends, shared by every " +
             "minigame. Optional.")]
    [SerializeField] private MinigameResultBanner minigameResultBanner;
    [Tooltip("Looping tick for the minigame's last seconds — the same clip as the round " +
             "timer's, so the two warnings sound alike. Optional.")]
    [SerializeField] private AudioClip minigameTickingSFX;
    [Tooltip("Seconds left on the minigame clock when the tick starts.")]
    [SerializeField] private float minigameTickingFrom = 5f;

    [Header("Timing")]
    [Tooltip("Seconds between characters while the question, and any feedback, types in. " +
             "Free to tune for feel: the countdown does not begin until the question has " +
             "finished typing, so a slower typewriter no longer eats the answer window.")]
    [SerializeField] private float typewriterCharDelay = 0.015f;
    [Tooltip("Beat between the item landing and the banner, so the drop reads first.")]
    [SerializeField] private float preFeedbackDelay = 0.3f;
    [Tooltip("Beat after the banner before the next question types in.")]
    [SerializeField] private float postFeedbackDelay = 0.35f;
    [Tooltip("How long the explanation is left in the dialogue box once it has finished " +
             "typing, before the round moves on. Only applies to questions that carry " +
             "feedback; the rest are unaffected.\n\n" +
             "Sized for Grade 6 readers rather than for whoever is testing it: with this " +
             "at 6 the longest line sits on screen for roughly nine seconds all told, " +
             "which is a comfortable pace for thirty words of Filipino. Nothing is waiting " +
             "on it — the timer is stopped by this point — so err on the generous side.")]
    [SerializeField] private float feedbackReadSeconds = 6f;

    [Header("Minigames")]
    [Tooltip("Runs after the water bottle question, whether the answer was right or wrong.")]
    [SerializeField] private WaterPourMinigame waterMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string waterMinigameItemName = "Water Bottle";
    [Tooltip("Runs after the whistle question, whether the answer was right or wrong.")]
    [SerializeField] private WhistleTapMinigame whistleMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string whistleMinigameItemName = "Whistle";
    [Tooltip("Runs after the first-aid question, whether the answer was right or wrong.")]
    [SerializeField] private FirstAidMinigame firstAidMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string firstAidMinigameItemName = "First Aid Kit";
    [Tooltip("Runs after the flashlight question, whether the answer was right or wrong.")]
    [SerializeField] private FlashlightMinigame flashlightMinigame;
    [Tooltip("The flashlight question accepts either torch, so both names belong here — " +
             "matching any one of them is enough to hand over to the minigame.")]
    [SerializeField] private string[] flashlightMinigameItemNames =
        new string[] { "Small Flashlight", "Big Flashlight" };
    [Tooltip("Runs after the glowstick question, whether the answer was right or wrong. " +
             "The same minigame as the flashlight's, with the light set square — see " +
             "squareLight on its FlashlightBeam.")]
    [SerializeField] private FlashlightMinigame glowstickMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string glowstickMinigameItemName = "Glow Sticks";
    [Tooltip("Runs after the N95 mask question, whether the answer was right or wrong.")]
    [SerializeField] private DustMaskMinigame dustMaskMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string dustMaskMinigameItemName = "N95 Mask";
    [Tooltip("Runs after the pocket knife question, whether the answer was right or wrong. " +
             "The rope is only what the knife is demonstrated on — the item being taught " +
             "is the knife.")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("ropeMinigame")]
    private PocketKnifeMinigame pocketKnifeMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField, UnityEngine.Serialization.FormerlySerializedAs("ropeMinigameItemName")]
    private string pocketKnifeMinigameItemName = "Pocket Knife";
    [Tooltip("Runs after the rope question, whether the answer was right or wrong.")]
    [SerializeField] private RopeKnotMinigame ropeKnotMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string ropeKnotMinigameItemName = "Rope";
    [Tooltip("Runs after the medication question, whether the answer was right or wrong.")]
    [SerializeField] private MedicationMinigame medicationMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string medicationMinigameItemName = "Medication";
    [Tooltip("Runs after the ziplock question, whether the answer was right or wrong.")]
    [SerializeField] private ZiplockMinigame ziplockMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string ziplockMinigameItemName = "Ziplock Bag";
    [Tooltip("Runs after the thermal blanket question, whether the answer was right or wrong.")]
    [SerializeField] private ThermalBlanketMinigame thermalBlanketMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string thermalBlanketMinigameItemName = "Thermal Blanket";
    [Tooltip("Runs after the radio question, whether the answer was right or wrong.")]
    [SerializeField] private RadioMinigame radioMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string radioMinigameItemName = "Radio";
    [Tooltip("Runs after the contact card question, whether the answer was right or wrong.")]
    [SerializeField] private ContactCardMinigame contactCardMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string contactCardMinigameItemName = "Contact Card";
    [Tooltip("Runs after the canned goods question, whether the answer was right or wrong.")]
    [SerializeField] private CannedFoodMinigame cannedFoodMinigame;
    [Tooltip("Either tin answers the canned goods question, the way the flashlight question " +
             "takes either torch, so both names belong here — matching any one of them is " +
             "enough to hand over to the minigame.")]
    [SerializeField] private string[] cannedFoodMinigameItemNames =
        new string[] { "Canned Corned Beef", "Canned Fish" };
    [Tooltip("Runs after the pen and paper question, whether the answer was right or wrong.")]
    [SerializeField] private PenAndPaperMinigame penAndPaperMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string penAndPaperMinigameItemName = "Pen & Paper";
    [Tooltip("Runs after the important documents question, whether the answer was right or wrong.")]
    [SerializeField] private ImportantDocumentsMinigame importantDocumentsMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string importantDocumentsMinigameItemName = "Important Documents";
    [Tooltip("Runs after the batteries question, whether the answer was right or wrong.")]
    [SerializeField] private BatteriesMinigame batteriesMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string batteriesMinigameItemName = "Batteries";
    [Tooltip("Runs after the spare clothes question, whether the answer was right or wrong.")]
    [SerializeField] private ClothesMinigame clothesMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string clothesMinigameItemName = "Spare Clothes";
    [Tooltip("Runs after the gloves question, whether the answer was right or wrong.")]
    [SerializeField] private GlovesMinigame glovesMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string glovesMinigameItemName = "Gloves";
    [Tooltip("Runs after the toiletries question, whether the answer was right or wrong.")]
    [SerializeField] private ToiletriesMinigame toiletriesMinigame;
    [Tooltip("The question whose correct answer is this item hands over to the minigame " +
             "once its feedback has finished.")]
    [SerializeField] private string toiletriesMinigameItemName = "Toiletries";

    [Header("Scoring")]
    [Tooltip("Par time for the 20 time points, as a fraction of the difficulty's limit. " +
             "Reach the finish door within this and the time points are all yours; after it " +
             "they taper to nothing as the clock runs out.\n\n" +
             "0.5 means half the limit — five minutes of ten on beginner — which is what " +
             "makes a flawless run actually able to score 100. Scoring the clock straight " +
             "would need a zero-second run for full marks, so the last points were " +
             "unreachable however well the student played.\n\n" +
             "Set to 0 for the old behaviour: points straight off the remaining clock.")]
    [SerializeField, Range(0f, 0.95f)] private float timeParFraction = DrillScore.DEFAULT_TIME_PAR;

    [Header("Results")]
    [SerializeField] private GameObject inventoryPanel;
    [SerializeField] private GameObject resultsPanel;
    [SerializeField] private AudioClip highScoreSFX;  // Plays when the drill score is Proficient or better (70+)
    [SerializeField] private AudioClip lowScoreSFX;   // Plays when the drill score is below Proficient (under 70)

    /// <summary>
    /// How long a round is at each difficulty. The question list is a pool: hold more than
    /// the round asks for and each run draws a random selection from it.
    ///
    /// These are what the round *wants*. A round can never be longer than the pool, so the
    /// figure actually used is clamped — see <see cref="QuestionsPerRound"/>.
    /// </summary>
    public const int QUESTIONS_BEGINNER = 10;
    public const int QUESTIONS_INTERMEDIATE = 15;
    public const int QUESTIONS_ADVANCED = 20;

    private const string DIFFICULTY_PREF = "SessionDifficulty";

    /// <summary>
    /// The round length a difficulty asks for, before it is clamped to the pool.
    /// Unknown values fall back to beginner, matching GameDifficultyApplier.
    /// </summary>
    public static int QuestionsForDifficulty(string difficulty)
    {
        switch ((difficulty ?? "").ToLower())
        {
            case "intermediate": return QUESTIONS_INTERMEDIATE;
            case "advanced":     return QUESTIONS_ADVANCED;
            default:             return QUESTIONS_BEGINNER;
        }
    }

    /// <summary>
    /// The round length for the session's difficulty, as picked on the difficulty panel and
    /// left in PlayerPrefs. Not clamped — callers that need a real count want
    /// <see cref="QuestionsPerRound"/>.
    /// </summary>
    public static int CurrentQuestionsPerRound
    {
        get { return QuestionsForDifficulty(PlayerPrefs.GetString(DIFFICULTY_PREF, "beginner")); }
    }

    /// <summary>
    /// How many questions this round will actually ask: what the difficulty wants, capped at
    /// the number of questions there are to ask. Without the cap an advanced round would try
    /// to draw twenty from a shorter pool and simply run out.
    /// </summary>
    private int QuestionsPerRound
    {
        get { return Mathf.Min(CurrentQuestionsPerRound, allQuestions.Length); }
    }

    /// <summary>
    /// How many questions this round actually asks — <see cref="QuestionsPerRound"/>, which is
    /// already capped at the pool size. Read off the built round so the score
    /// denominator can never disagree with what was played. Falls back to the pool size
    /// before <see cref="RandomizeQuestions"/> has run.
    /// </summary>
    private int TotalQuestions
    {
        get
        {
            if (randomizedQuestions.Count > 0)
                return randomizedQuestions.Count;

            return QuestionsPerRound;
        }
    }

    private int currentQuestionIndex = 0;
    private int correctCount = 0;

    // Questions whose practical task was seen through. A question with no minigame counts
    // here too: there is nothing to fail, and the component scores taking part rather than
    // getting the answer right — a wrong answer still plays its minigame.
    private int tasksCompleted = 0;

    // What was in the bag when the quiz opened, and the limit it was packed against.
    //
    // Taken as a snapshot rather than read at the end, because a correct answer consumes its
    // item out of the grid (see ConsumeItem). Scoring the bag afterwards measured only the
    // leftovers, so the better a student answered the worse their packing looked — a perfect
    // run scored 2 of 13 essentials.
    private List<DrillScore.PackedItem> packedAtOpen = new List<DrillScore.PackedItem>();
    private float weightLimitAtOpen = 0f;
    private int essentialTargetAtOpen = 0;
    // True from the moment an item lands until the next question is ready for input.
    private bool isResolvingAnswer = false;

    // Randomized question order — index matches currentQuestionIndex
    private List<QuestionData> randomizedQuestions = new List<QuestionData>();

    private Coroutine typewriterRoutine;
    private Coroutine questionTimerRoutine;

    // Set by the wrapper around a minigame's Play once it returns on its own
    private bool minigameRunning = false;

    // True if any minigame for the question just answered was cut off by its timer
    private bool minigameTimedOut = false;

    // Plays minigameTickingSFX. Made on first use and handed to the SFX bus.
    private AudioSource minigameTickingSource;

    // The onboarding's practice quiz: one question, nothing timed, nothing scored or uploaded
    private bool practiceQuiz = false;

    // Set by the onboarding once the player has read the practice question's feedback
    private bool practiceFeedbackRead = false;

    /// <summary>Raised when an answer lands (or the question times out), with the verdict.</summary>
    public event System.Action<bool> AnswerResolved;
    /// <summary>
    /// Practice quiz only: raised once the verdict has played and its explanation is on screen.
    /// The quiz then waits for <see cref="ContinueAfterFeedback"/>.
    /// </summary>
    public event System.Action<bool> FeedbackShown;
    /// <summary>Raised as a minigame opens, with the minigame's component.</summary>
    public event System.Action<MonoBehaviour> MinigameStarted;
    /// <summary>Raised once a minigame has closed.</summary>
    public event System.Action MinigameFinished;
    /// <summary>Raised instead of the results screen when the practice quiz is over.</summary>
    public event System.Action PracticeQuizFinished;

    /// <summary>The pieces of the quiz the onboarding points at.</summary>
    public GameObject DialogueBox => dialogueBox;
    public QuizAnswerBox AnswerBox => answerBox;
    public CountdownBar QuestionTimerBar => questionTimerBar;
    public CountdownBar MinigameTimerBar => minigameTimerBar;

    // The earthquake preparedness question pool. Each round draws a random selection from it
    // (see RandomizeQuestions), so the order here does not matter. Add to this list and the round grows to match: the length drives how many
    // questions are asked, the score denominator, and the debug picker's dropdown.
    private QuestionData[] allQuestions = new QuestionData[]
    {
        new QuestionData
        {
            questionText = "Nawalan ng kuryente dahil sa lindol at sobrang dilim ng paligid. Anong gamit ang magbibigay ng ligtas na liwanag upang makakita sa dilim?",
            correctAnswerItemNames = new string[] { "Small Flashlight", "Big Flashlight" },
            correctFeedback = "Tama! Ang flashlight ang nagbibigay ng matinding liwanag upang makakita sa dilim.",
            incorrectFeedback = "Mahalaga ang flashlight! Kapag nagka-blackout matapos ang lindol, ito ang tanging ligtas na gabay upang makakita sa paligid na madilim."
        },
        new QuestionData
        {
            questionText = "Bumagsak ang pader at naipit ka sa loob ng silid. Paos ka na sa kakasigaw at nauubusan ng hangin. Anong gamit ang lilikha ng matinis at malakas na tunog upang marinig ka ng search and rescue team?",
            correctAnswerItemNames = new string[] { "Whistle" },
            correctFeedback = "Tumpak! Mas malayo ang nararating ng tunog ng pito kaysa sa boses, at hindi ka mauubusan ng lakas habang naghihintay ng tulong.",
            incorrectFeedback = "Dapat may pito ka! Ang pito ang pinakamabisang gamit pantawag ng rescuer nang hindi nauubos ang iyong hininga."
        },
        new QuestionData
        {
            // Stands on its own: questions are shuffled, so it cannot lean on the water
            // question having mentioned the wound first
            questionText = "May galos at sugat ka sa braso mula sa gumuhong pader. Anong gamit mula sa go-bag ang naglalaman ng sterile gauze at plaster upang ibendahe ang sugat at mapigilan ang pagdurugo at dumi?",
            correctAnswerItemNames = new string[] { "First Aid Kit" },
            correctFeedback = "Tama! Ang First Aid Kit ang nagbibigay ng agarang proteksyon sa sugat upang hindi ito mapasukan ng dumi o ma-impeksyon.",
            incorrectFeedback = "Dapat may first aid kit! Ito ang kumpletong kagamitan sa pagbendahe ng sugat habang naghihintay ng medic."
        },
        new QuestionData
        {
            questionText = "Nakalabas ka na sa open field ngunit naputol ang linya ng tubig sa gripo dahil sa lindol. Tuyong-tuyo ang lalamunan mo at may galos ka na napasukan ng maruming buhangin. Anong gamit ang kailangan upang ligtas na makainom at mahugasan ang dumi sa sugat?",
            correctAnswerItemNames = new string[] { "Water Bottle" },
            correctFeedback = "Magaling! Ang malinis na nakaboteng tubig ang numero unong kailangan para sa hydration at paglilinis ng sugat kapag bagsak ang water supply.",
            incorrectFeedback = "Laging unahin ang tubig! Sa lindol, pumuputok ang mga tubo sa ilalim ng lupa. Ang nakaimbak na malinis na tubig ang bubuhay sa iyo sa 72 oras."
        },
        new QuestionData
        {
            questionText = "Kasunod ng malakas na pagyanig, gumuho ang kisame at napuno ng makapal na alikabok at pinong semento ang daanan. Anong gamit ang dapat mong isuot agad upang hindi malanghap ang mapanganib na alikabok habang lumilikas?",
            correctAnswerItemNames = new string[] { "N95 Mask" },
            correctFeedback = "Magaling! Sinasala ng N95 mask ang mapanganib na alikabok at pinong semento upang maprotektahan ang iyong baga habang lumalabas ng gumuhong gusali.",
            incorrectFeedback = "Tandaan ito! Pagkatapos ng lindol, makapal ang alikabok ng semento. Ang N95 mask ang kailangan upang makahinga nang ligtas at maiwasan ang hirap sa paghinga."
        },
        new QuestionData
        {
            // Rules out batteries (the flashlight is broken, not flat) and the candle and matches
            // (leaking gas) on purpose, so neither obvious alternative is marked wrong here
            questionText = "Nadurog ng gumuhong pader ang iyong flashlight, at may naaamoy kang tumatagas na gas kaya bawal magsindi ng apoy. Anong ligtas na ilaw na hindi kailangan ng baterya o apoy ang maaari mong gamitin sa dilim?",
            correctAnswerItemNames = new string[] { "Glow Sticks" },
            correctFeedback = "Tama! Ang glow stick ay nagbibigay ng liwanag nang walang baterya o apoy, kaya ligtas itong gamitin kahit may tagas ng gas.",
            incorrectFeedback = "Isama ang glow stick! Ito ang alternatibong ilaw kapag hindi na magamit ang iyong flashlight sa dilim, at hindi ito nangangailangan ng baterya o apoy."
        },
        new QuestionData
        {
            questionText = "Maiihip ng malakas na hangin ang inyong tent sa evacuation site. Anong matibay na kagamitan ang gagamitin upang itali ito nang maayos sa puno o poste?",
            correctAnswerItemNames = new string[] { "Rope" },
            correctFeedback = "Tama! Ang 7-meter rope ay matibay na pantali sa pagtatayo ng shelter at pag-secure ng mga gamit sa panahon ng kalamidad.",
            incorrectFeedback = "Kailangan ang lubid! Ito ang humahawak sa mga tent at pansamantalang shelter kapag walang matirhan matapos gumuho ang mga bahay at gusali."
        },
        new QuestionData
        {
            questionText = "Kailangan mo ng kagamitan upang makapagputol ng matigas na lubid. Anong gamit ang mabilis na makakaputol nito?",
            correctAnswerItemNames = new string[] { "Pocket Knife" },
            correctFeedback = "Magaling! Ang multi-tool o pocket knife ay maraming gamit para sa pagputol ng lubid.",
            incorrectFeedback = "Isama ang multi-tool o pocket knife! Napakahalaga nito sa pagputol ng mga materyales habang nagtatayo ng proteksyon sa init at ulan."
        },
        new QuestionData
        {
            questionText = "Dahil sa takot at alikabok ng lindol, inatake ng hika ang iyong kapatid o tumaas ang alta presyon ng lola mo, at sarado ang lahat ng botika. Anong gamit ang dapat nakahanda para sa kanilang tiyak na sakit?",
            correctAnswerItemNames = new string[] { "Medication" },
            correctFeedback = "Mahusay! Ang sariling maintenance medicines ay hindi maibibigay agad ng relief teams kaya dapat nakahanda ito sa go-bag.",
            incorrectFeedback = "Huwag kalimutan ito! Ang personal na gamot sa hika o maintenance ay kailangang laging nasa go-bag dahil sarado ang mga botika matapos ang lindol."
        },
        new QuestionData
        {
            questionText = "Biglang bumuhos ang malakas na ulan sa evacuation center. Paano mo poprotektahan ang iyong gamot, contact card, baterya, at N95 mask upang hindi mabasa at masira ng tubig-ulan?",
            correctAnswerItemNames = new string[] { "Ziplock Bag" },
            correctFeedback = "Tama! Hindi pinapasok ng tubig ang nakasarang ziplock bag, kaya napoprotektahan nito ang mga sensitibong gamit laban sa ulan.",
            incorrectFeedback = "Mahalaga ang ziplock! Pinapanatili nitong tuyo at hindi madumi ang mga gamit na madaling masira sa tubig o alikabok habang nasa evacuation center."
        },
        new QuestionData
        {
            questionText = "Gabi na at magdamag kayong matutulog sa malamig na semento ng evacuation center dahil sa mga aftershock. Anong magaan at makintab na kumot ang tumutulong mapanatili ang init ng iyong katawan?",
            correctAnswerItemNames = new string[] { "Thermal Blanket" },
            correctFeedback = "Tama! Ang thermal foil blanket ay nagpapanatili ng init ng katawan at pumipigil sa hypothermia kapag natutulog sa labas.",
            incorrectFeedback = "Kailangan ang thermal blanket! Napakagaan nito ngunit mabisang panlaban sa matinding lamig kapag bawal pumasok sa mga gusali dahil sa aftershocks."
        },
        new QuestionData
        {
            questionText = "Basang-basa at puno ng maruming putik ang suot mong damit mula sa paglikas sa gumuhong lugar. Ano ang dapat mong ipalit upang hindi magkasakit at ginawin sa gabi?",
            correctAnswerItemNames = new string[] { "Spare Clothes" },
            correctFeedback = "Tama! Ang tuyong ekstrang damit ay nagpoprotekta laban sa pulmonya, lagnat, at impeksyon sa balat sa evacuation shelter.",
            incorrectFeedback = "Magbaon ng tuyong damit! Ang pagpapalit ng tuyong damit ay nagliligtas sa iyo laban sa sakit."
        },
        new QuestionData
        {
            // Either tin answers this one, the way the flashlight question takes either torch
            questionText = "Ikalawang araw na matapos ang lindol; sarado ang mga palengke at walang kuryente o gas para magluto. Anong pagkain ang ligtas at handang kainin agad upang magbigay ng lakas?",
            correctAnswerItemNames = new string[] { "Canned Corned Beef", "Canned Fish" },
            correctFeedback = "Magaling! Ang easy-open canned goods ay nagbibigay ng agarang protina at sustansya nang hindi nangangailangan ng kalan.",
            incorrectFeedback = "Mahalaga ang de-lata! Nagtatagal ito nang walang refrigerator at hindi nangangailangan ng pagluluto sa panahon ng kalamidad."
        },
        new QuestionData
        {
            questionText = "Dumating ang mga opisyal ng barangay upang irehistro ang mga biktima ng lindol para sa relief assistance at ayuda. Anong gamit ang magpapatunay ng iyong pagkakakilanlan at pamilya?",
            correctAnswerItemNames = new string[] { "Important Documents" },
            correctFeedback = "Tama! Ang kopya ng birth certificate at iba pang dokumento ang patunay ng pagkakakilanlan upang mapadali ang tulong at emergency aid.",
            incorrectFeedback = "Isama ang mga dokumento! Kapag nawalan ng bahay dahil sa lindol, ang kopya ng birth certificate ang magpapatunay sa inyong pamilya para sa tulong."
        },
        new QuestionData
        {
            questionText = "Siksikan ang libu-libong lumikas sa evacuation area at marumi ang paligid. Anong kit ang naglalaman ng sabon at toothbrush upang maiwasan ang nakakahawang sakit at diarrhea?",
            correctAnswerItemNames = new string[] { "Toiletries" },
            correctFeedback = "Tumpak! Ang toiletries kit ay panlaban sa diarrhea at mga mikrobyo sa siksikang evacuation shelter.",
            incorrectFeedback = "Dapat may hygiene kit! Sa gitna ng kalamidad, ang kalinisan sa katawan ang humahadlang sa malawakang pagkalat ng sakit."
        },
        new QuestionData
        {
            questionText = "Anong alternatibong gamit ang maaari mong gamitin upang makagawa ng mapa papunta sa evacuation center kung sakaling hindi mo magamit ang iyong telepono at walang signal?",
            correctAnswerItemNames = new string[] { "Pen & Paper" },
            correctFeedback = "Tama! Ang panulat at papel ay ang bagay na pwede mong gamitin upang makagawa ng mapa na maaari niyong gawing gabay sakaling kayo ay maligaw.",
            incorrectFeedback = "Isama ito sa go-bag! Kapag walang kuryente at cellphone, maaari mo itong gamitin na gabay at pangkomunikasyon."
        },
        new QuestionData
        {
            questionText = "Naubusan ng baterya ang iyong cellphone at hindi mo na ito mabuksan. Paano mo malalaman ang opisyal na numero ng Valenzuela CDRRMO at mga kamag-anak upang humingi ng tulong?",
            correctAnswerItemNames = new string[] { "Contact Card" },
            correctFeedback = "Tama! Ang nakasulat sa Contact Card ay napakahalaga at makatutulong ito sa mga oras na kailangan ng tulong at sakuna pagkatapos ng lindol.",
            incorrectFeedback = "Laging magbaon nito! Ang nakasulat na emergency numbers sa papel ang sasagip sa iyo kapag walang baterya ang smartphone."
        },
        new QuestionData
        {
            questionText = "Namatay na ang iyong radyo at flashlight dahil naubusan ng karga, at nananatiling walang kuryente sa inyong barangay. Anong gamit ang kailangan mo para mapagana ulit ang mga ito?",
            correctAnswerItemNames = new string[] { "Batteries" },
            correctFeedback = "Tama! Ang mga extra na baterya ay ang magsisilbing kuryente upang patuloy na magamit ang radyo at flashlight sa oras ng emergency.",
            incorrectFeedback = "Huwag kalimutan ang mga extra na baterya! Walang silbi ang iyong radyo at flashlight kung wala itong power."
        },
        new QuestionData
        {
            questionText = "Bagsak ang internet at cell signal sa buong siyudad, at kumakalat ang fake news tungkol sa lindol. Anong gamit ang makakasagap ng opisyal na balita at aftershock advisories mula sa PHIVOLCS?",
            correctAnswerItemNames = new string[] { "Radio" },
            correctFeedback = "Tumpak! Ang radyo ang pinakamatatag na linya ng komunikasyon upang makasagap ng totoong balita mula sa gobyerno kapag walang internet.",
            incorrectFeedback = "Isama ang radyo! Ito ang tanging paraan upang malaman ang opisyal na babala ng PHIVOLCS kapag walang internet at kuryente."
        },
        new QuestionData
        {
            questionText = "Tumigil na ang pagyanig, ngunit may basag na salamin na humaharang sa iyong daraanan. Kailangan mong maingat na alisin ang mga basag na salamin. Ano ang unang-unang dapat mong isuot sa iyong mga kamay?",
            correctAnswerItemNames = new string[] { "Gloves" },
            correctFeedback = "Tama! Napakatibay ng mga heavy-duty gloves! Nagsisilbi itong pangbalot sa iyong mga kamay para hindi ka masugatan ng mga matutulis na salamin, kalawanging pako, o magagaspang na semento habang nililinis mo ang iyong daraanan.",
            incorrectFeedback = "Huwag na huwag mong hahawakan ang mga kalat mula sa lindol gamit ang iyong mga kamay nang walang proteksyon! Kahit mag-ingat ka pa, ang mga nakatagong bubog o kalawanging bakal ay maaaring magdulot ng malubhang sugat. Laging protektahan muna ang iyong mga kamay!"
        }
    };

    // Awake, not Start: FinishDoorHandler activates this panel and calls OpenQuiz in the
    // same frame, and Start would run *after* that call and hide the box again.
    void Awake()
    {
        // Everything stays hidden until OpenQuiz runs
        if (dialogueBox != null)
            dialogueBox.SetActive(false);

        if (characterPortrait != null)
            characterPortrait.gameObject.SetActive(false);

        if (feedbackBanner != null)
            feedbackBanner.Hide();

        if (feedbackScrim != null)
            feedbackScrim.Clear();

        if (questionTimerText != null)
            questionTimerText.gameObject.SetActive(false);

        if (questionTimerBar != null)
            questionTimerBar.Hide();

        if (minigameTimerText != null)
            minigameTimerText.gameObject.SetActive(false);

        if (minigameTimerBar != null)
            minigameTimerBar.Hide();
    }

    /// <summary>
    /// Called when Yes button is clicked in FinishDoorHandler.
    /// Shuffles the questions and starts the dialogue at question 1.
    /// </summary>
    public void OpenQuiz()
    {
        practiceQuiz = false;
        currentQuestionIndex = 0;
        correctCount = 0;
        tasksCompleted = 0;
        isResolvingAnswer = false;

        // Before the first question — correct answers start eating the bag from here on
        CaptureBagSnapshot();

        RandomizeQuestions();

        if (dialogueBox != null)
            dialogueBox.SetActive(true);

        if (characterPortrait != null)
            characterPortrait.gameObject.SetActive(true);

        if (feedbackBanner != null)
            feedbackBanner.Hide();

        if (questionTimerText != null)
            questionTimerText.gameObject.SetActive(true);

        ShowQuestion(0);
    }

    /// <summary>
    /// Time ran out with nothing in the go-bag: every answer is an item from the bag, so there
    /// is nothing to answer with. Scores the round as it stands (no answers, no tasks) and
    /// goes straight to the results instead of sitting the student through a quiz they cannot
    /// play. The quiz panel must be active — the results are shown from a coroutine.
    /// </summary>
    public void SkipToResults()
    {
        practiceQuiz = false;
        currentQuestionIndex = 0;
        correctCount = 0;
        tasksCompleted = 0;
        isResolvingAnswer = true;

        CaptureBagSnapshot();
        RandomizeQuestions();
        FinishQuiz();
    }

    /// <summary>Lets the practice quiz move on from the feedback it is holding on.</summary>
    public void ContinueAfterFeedback()
    {
        practiceFeedbackRead = true;
    }

    /// <summary>
    /// The onboarding's quiz: just the question <paramref name="answerItemName"/> answers, then
    /// its minigame. The clocks are shown but held full, nothing is scored, the Journal is left
    /// alone and nothing reaches the teacher dashboard; <see cref="PracticeQuizFinished"/> is
    /// raised where the results would have been.
    /// </summary>
    public void OpenPracticeQuiz(string answerItemName)
    {
        practiceQuiz = true;
        currentQuestionIndex = 0;
        correctCount = 0;
        tasksCompleted = 0;
        isResolvingAnswer = false;

        randomizedQuestions.Clear();
        foreach (QuestionData question in allQuestions)
        {
            foreach (string answer in question.correctAnswerItemNames)
            {
                if (answer.Equals(answerItemName, System.StringComparison.OrdinalIgnoreCase))
                {
                    randomizedQuestions.Add(question);
                    break;
                }
            }

            if (randomizedQuestions.Count > 0)
                break;
        }

        if (randomizedQuestions.Count == 0)
        {
            Debug.LogWarning($"No quiz question is answered by {answerItemName}; the practice quiz has nothing to ask.", this);
            randomizedQuestions.Add(allQuestions[0]);
        }

        if (dialogueBox != null)
            dialogueBox.SetActive(true);

        if (characterPortrait != null)
            characterPortrait.gameObject.SetActive(true);

        if (feedbackBanner != null)
            feedbackBanner.Hide();

        if (questionTimerText != null)
            questionTimerText.gameObject.SetActive(true);

        ShowQuestion(0);
    }

    /// <summary>
    /// Randomizes the order of the questions using Fisher-Yates shuffle.
    /// </summary>
    private void RandomizeQuestions()
    {
        randomizedQuestions.Clear();

        for (int i = 0; i < allQuestions.Length; i++)
        {
            randomizedQuestions.Add(allQuestions[i]);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // A test selection names both the questions and the order to ask them in, so it
        // replaces the draw outright — shuffling it would throw away the chosen order.
        if (ApplyDebugQuestionSubset())
            return;
#endif

        for (int i = randomizedQuestions.Count - 1; i > 0; i--)
        {
            int randomIndex = Random.Range(0, i + 1);

            QuestionData temp = randomizedQuestions[i];
            randomizedQuestions[i] = randomizedQuestions[randomIndex];
            randomizedQuestions[randomIndex] = temp;
        }

        // The round is as long as the difficulty asks for: with a larger pool, the shuffle
        // above decides which questions make the cut and the rest sit this run out.
        int wanted = QuestionsPerRound;
        if (randomizedQuestions.Count > wanted)
            randomizedQuestions.RemoveRange(wanted, randomizedQuestions.Count - wanted);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// Test-only hook, driven by QuizDebugPicker's floating button: the indices into
    /// <see cref="allQuestions"/> to ask, <b>in the order they should be asked</b>. Ignored
    /// unless it names exactly <see cref="QuestionsPerRound"/> of them, since a round is a
    /// fixed length. Null means the normal random draw. Compiled out of a release build
    /// along with the picker.
    /// </summary>
    public static List<int> DebugQuestionSubset = null;

    /// <summary>
    /// The unshuffled question list, so the picker can label its rows.
    /// </summary>
    public QuestionData[] GetAllQuestionsForDebug()
    {
        return allQuestions;
    }

    /// <summary>
    /// Swaps the pool for the chosen questions, keeping the order they were chosen in — the
    /// selection is the running order, so the first one picked is the first one asked.
    ///
    /// Indices that no longer exist are skipped, and a selection that does not come to a
    /// full round is ignored outright: a short round would quietly change the score
    /// denominator, so a half-made choice falls back to the normal random draw.
    /// </summary>
    /// <returns>True if the round was taken from the selection, and so must not be shuffled.</returns>
    private bool ApplyDebugQuestionSubset()
    {
        if (DebugQuestionSubset == null || DebugQuestionSubset.Count == 0)
            return false;

        List<QuestionData> picked = new List<QuestionData>();
        foreach (int index in DebugQuestionSubset)
        {
            if (index >= 0 && index < allQuestions.Length)
                picked.Add(allQuestions[index]);
        }

        // Anything other than a full round is not a usable choice
        if (picked.Count == 0 || picked.Count != QuestionsPerRound)
            return false;

        randomizedQuestions.Clear();
        randomizedQuestions.AddRange(picked);
        return true;
    }
#endif

    /// <summary>
    /// Loads a question into the dialogue box and re-arms the answer box.
    /// </summary>
    private void ShowQuestion(int index)
    {
        currentQuestionIndex = index;

        if (answerBox != null)
            answerBox.PrepareForQuestion(index);

        if (typewriterRoutine != null)
            StopCoroutine(typewriterRoutine);

        string text = index < randomizedQuestions.Count ? randomizedQuestions[index].questionText.Trim() : "";

        // The countdown is started by the typewriter itself, once the question is fully on
        // screen. Starting it here instead charged the player for the seconds spent reading
        // a question that was still spelling itself out — and the longer the question, the
        // more it cost them.
        StopQuestionTimer();

        // Back to full and on screen for the new question. Put up while it types rather than
        // when the countdown starts, so the player can see what they are about to be given.
        if (questionTimerBar != null && questionTimeLimit > 0f)
            questionTimerBar.Begin();

        UpdateQuestionTimerDisplay(questionTimeLimit);

        typewriterRoutine = StartCoroutine(TypeQuestion(text, true));
    }

    /// <summary>
    /// Restarts the per-question countdown. Every question gets the same
    /// <see cref="questionTimeLimit"/>, measured from the moment it has finished typing.
    /// </summary>
    private void StartQuestionTimer()
    {
        StopQuestionTimer();

        // The practice quiz leaves its bar standing full: shown, so it can be pointed out,
        // but never counting against someone still learning where to drag
        if (questionTimeLimit <= 0f || practiceQuiz)
            return;

        questionTimerRoutine = StartCoroutine(RunQuestionTimer());
    }

    private void StopQuestionTimer()
    {
        if (questionTimerRoutine != null)
        {
            StopCoroutine(questionTimerRoutine);
            questionTimerRoutine = null;
        }
    }

    /// <summary>
    /// Counts the current question down and, if it runs out, scores it as a miss and
    /// moves on. Scaled time, so the pause menu holds the countdown along with the game.
    /// </summary>
    private IEnumerator RunQuestionTimer()
    {
        float remaining = questionTimeLimit;
        UpdateQuestionTimerDisplay(remaining);

        while (remaining > 0f)
        {
            yield return null;

            // An answer landing mid-countdown takes over from here
            if (isResolvingAnswer)
            {
                questionTimerRoutine = null;
                yield break;
            }

            remaining -= Time.deltaTime;
            UpdateQuestionTimerDisplay(remaining);
        }

        UpdateQuestionTimerDisplay(0f);
        questionTimerRoutine = null;

        // Out of time counts as a wrong answer, with an empty box behind the banner
        isResolvingAnswer = true;
        StartCoroutine(ResolveAnswer(currentQuestionIndex, null));
    }

    /// <summary>
    /// Moves the question's countdown on. The bar writes the seconds itself when it has been
    /// given the readout, so the number fades through the same colours the bar does instead
    /// of snapping to red on its own threshold — one clock, one colour.
    /// </summary>
    private void UpdateQuestionTimerDisplay(float remaining)
    {
        if (questionTimerBar != null)
        {
            questionTimerBar.SetTime(remaining, questionTimeLimit);

            if (questionTimerBar.OwnsLabel(questionTimerText))
                return;
        }

        UpdateTimerLabel(questionTimerText, remaining);
    }

    /// <summary>
    /// Writes whole seconds into a countdown label, in the warning colour near the end.
    /// Shared by the question and minigame timers so the two read the same.
    /// </summary>
    private void UpdateTimerLabel(TextMeshProUGUI label, float remaining)
    {
        if (label == null)
            return;

        float clamped = Mathf.Max(0f, remaining);
        label.text = Mathf.CeilToInt(clamped).ToString();
        label.color = clamped <= questionTimerWarningThreshold
            ? questionTimerWarningColor
            : questionTimerNormalColor;
    }

    /// <summary>
    /// Plays a minigame against <see cref="minigameTimeLimit"/>. If the clock wins, the
    /// minigame's coroutine is stopped, <paramref name="forceClose"/> tears its panel down,
    /// and <see cref="minigameTimedOut"/> is raised so the task is not counted.
    /// Scaled time, like the question timer, so the pause menu holds it too.
    ///
    /// THE CLOCK STOPS WHEN THE PLAYER IS DONE, NOT WHEN THE MINIGAME IS
    ///
    /// A minigame runs on for a few seconds after the last piece is placed: it raises its
    /// COMPLETED banner, holds it, then fades its panel and backdrop away, and only then does
    /// Play return. Counting those seconds meant a player who finished with little to spare
    /// could still be timed out during their own congratulations — force-closed mid-banner
    /// and, worse, not credited with the task. So the countdown ends the moment the objective
    /// is ticked, and whatever the minigame has left to play out, it plays out untimed.
    /// </summary>
    private IEnumerator PlayTimedMinigame(IEnumerator play, System.Action forceClose,
                                          MonoBehaviour minigame)
    {
        minigameRunning = true;
        Coroutine routine = StartCoroutine(WatchMinigame(play));

        // A minigame with nothing to do can return inside StartCoroutine itself
        if (!minigameRunning)
            yield break;

        // Every minigame panel carries the objective card, and the card knows when its
        // minigame has been seen through, so no minigame had to learn about the clock
        MinigameObjective objective = minigame != null
            ? minigame.GetComponentInChildren<MinigameObjective>(true)
            : null;

        float remaining = minigameTimeLimit;

        // The practice quiz shows the clock, full, but never runs it down
        bool shown = minigameTimeLimit > 0f;
        bool timed = shown && !practiceQuiz;

        if (minigameTimerText != null && shown)
        {
            UpdateTimerLabel(minigameTimerText, remaining);
            minigameTimerText.gameObject.SetActive(true);
        }

        if (shown && minigameTimerBar != null)
        {
            minigameTimerBar.Begin();
            minigameTimerBar.SetTime(remaining, minigameTimeLimit);
        }

        MinigameStarted?.Invoke(minigame);

        bool completed = false;

        while (minigameRunning)
        {
            yield return null;

            if (!minigameRunning)
                break;

            // Done. Take the clock down, raise COMPLETE!, and let the rest of the minigame
            // play out in peace. The banner fades out with the minigame's own panel.
            if (!completed && objective != null && objective.IsComplete)
            {
                completed = true;
                timed = false;
                HideMinigameClock();

                if (minigameResultBanner != null)
                {
                    GameObject ownBanner = objective.CompletedBanner;
                    minigameResultBanner.ShowComplete(ownBanner != null
                        ? ownBanner.GetComponentInParent<CanvasGroup>(true)
                        : null);
                }
                continue;
            }

            if (!timed)
                continue;

            remaining -= Time.deltaTime;
            UpdateTimerLabel(minigameTimerText, remaining);
            UpdateMinigameTicking(remaining);

            if (minigameTimerBar != null)
                minigameTimerBar.SetTime(remaining, minigameTimeLimit);

            if (remaining <= 0f)
            {
                // Freeze the minigame where it stands and play TIME'S UP! over it before
                // tearing it down, so the player sees what ended it
                StopCoroutine(routine);
                minigameRunning = false;
                minigameTimedOut = true;
                HideMinigameClock();

                if (objective != null)
                    objective.SetFailed();

                if (minigameResultBanner != null)
                    yield return StartCoroutine(minigameResultBanner.PlayTimesUp());

                if (forceClose != null)
                    forceClose();
            }
        }

        HideMinigameClock();

        if (minigameResultBanner != null)
            minigameResultBanner.Hide();

        MinigameFinished?.Invoke();
    }

    /// <summary>
    /// Takes the minigame's countdown off screen. Safe to call twice — the clock comes down
    /// when the player finishes, and again when the minigame itself is finally over.
    /// </summary>
    private void HideMinigameClock()
    {
        if (minigameTimerText != null)
            minigameTimerText.gameObject.SetActive(false);

        if (minigameTimerBar != null)
            minigameTimerBar.Hide();

        if (minigameTickingSource != null)
            minigameTickingSource.Stop();
    }

    /// <summary>
    /// Runs the tick through the minigame's last seconds. Held while the game is paused:
    /// the clock stops with timeScale, and a tick carrying on behind the pause menu
    /// would promise a countdown that isn't happening.
    /// </summary>
    private void UpdateMinigameTicking(float remaining)
    {
        if (minigameTickingSFX == null || remaining > minigameTickingFrom)
            return;

        if (minigameTickingSource == null)
        {
            minigameTickingSource = gameObject.AddComponent<AudioSource>();
            minigameTickingSource.clip = minigameTickingSFX;
            minigameTickingSource.loop = true;
            minigameTickingSource.playOnAwake = false;
            minigameTickingSource.spatialBlend = 0f;

            if (SoundManager.Instance != null)
                SoundManager.Instance.RegisterSFXSource(minigameTickingSource);
        }

        if (Time.timeScale == 0f)
        {
            minigameTickingSource.Pause();
            return;
        }

        if (!minigameTickingSource.isPlaying)
        {
            // UnPause picks up a tick held by the pause menu; Play starts a fresh one
            minigameTickingSource.UnPause();
            if (!minigameTickingSource.isPlaying)
                minigameTickingSource.Play();
        }
    }

    private IEnumerator WatchMinigame(IEnumerator play)
    {
        yield return play;
        minigameRunning = false;
    }

    /// <summary>
    /// Spells the text into the dialogue box.
    ///
    /// <paramref name="startTimerWhenDone"/> is set for a question and left alone for the
    /// feedback that follows one, which is read at leisure with nothing counting down.
    /// </summary>
    private IEnumerator TypeQuestion(string text, bool startTimerWhenDone = false)
    {
        if (questionText == null)
            yield break;

        questionText.text = text;

        // maxVisibleCharacters reveals the text without re-laying it out every frame,
        // so the paragraph never reflows mid-type.
        questionText.maxVisibleCharacters = 0;
        questionText.ForceMeshUpdate();

        int total = questionText.textInfo.characterCount;

        for (int i = 1; i <= total; i++)
        {
            questionText.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(typewriterCharDelay);
        }

        questionText.maxVisibleCharacters = int.MaxValue;
        typewriterRoutine = null;

        if (startTimerWhenDone)
            StartQuestionTimer();
    }

    /// <summary>
    /// Called by QuizAnswerBox the moment an item lands in the box.
    /// Scores the answer, plays the feedback, then advances.
    /// </summary>
    public void OnItemPlaced(int answerBoxIndex, InventoryItem item)
    {
        if (isResolvingAnswer || item == null)
            return;

        isResolvingAnswer = true;
        StopQuestionTimer();
        StartCoroutine(ResolveAnswer(answerBoxIndex, item));
    }

    /// <summary>
    /// Scores the question and plays the feedback through to the next one.
    /// A null <paramref name="item"/> means the question timed out, which grades
    /// as wrong with nothing in the box.
    /// </summary>
    private IEnumerator ResolveAnswer(int answerBoxIndex, InventoryItem item)
    {
        bool isCorrect = item != null && IsCorrectAnswer(answerBoxIndex, item.itemName);

        // Nothing is being timed from here until the next question, so the bar comes down
        // rather than sitting frozen behind the verdict and the minigame that follows it.
        if (questionTimerBar != null)
            questionTimerBar.Hide();

        if (isCorrect)
            correctCount++;

        AnswerResolved?.Invoke(isCorrect);

        // The typewriter may still be running — snap the question to fully visible
        // so the player can read what they just answered.
        if (typewriterRoutine != null)
        {
            StopCoroutine(typewriterRoutine);
            typewriterRoutine = null;
            if (questionText != null)
                questionText.maxVisibleCharacters = int.MaxValue;
        }

        if (answerBox != null)
            answerBox.ShowResult(isCorrect);

        // A right answer is used up and leaves the bag straight away; a wrong one stays
        // packed. Waits one frame first: the item is still finishing its drag this frame, and
        // the drag handler puts it back in its grid before letting go.
        if (isCorrect && answerBox != null)
        {
            yield return null;
            answerBox.ConsumeItem();
        }

        yield return new WaitForSecondsRealtime(preFeedbackDelay);

        // Freeze the quiz into a blurred still so the verdict is the only thing in focus.
        // Captured with the UI left on, and only after the box has been tinted, so the
        // snapshot shows the answer the player just gave.
        if (feedbackScrim != null)
            yield return StartCoroutine(feedbackScrim.CaptureIncludingUIRoutine());

        // The chime goes off as the verdict word pops onto the screen, not when the item is
        // dropped: the scrim capture and pre-feedback pause sit between the two
        SoundManager.Sfx(isCorrect ? correctSFX : wrongSFX);

        if (feedbackBanner != null)
            yield return StartCoroutine(feedbackBanner.Play(isCorrect));

        // Eases off after the band has gone, rather than snapping the quiz back into focus
        if (feedbackScrim != null)
            yield return StartCoroutine(feedbackScrim.FadeOutRoutine());

        // The banner gives the verdict; the reason goes in the dialogue box, typed the same
        // way the question was. It waits until the scrim has cleared, or the explanation
        // would be spelling itself out behind a blur nobody can read.
        string feedback = GetFeedback(answerBoxIndex, isCorrect);
        bool hasFeedback = !string.IsNullOrEmpty(feedback) && questionText != null;
        if (hasFeedback)
        {
            typewriterRoutine = StartCoroutine(TypeQuestion(feedback));
            yield return typewriterRoutine;
        }

        if (practiceQuiz)
        {
            // The practice holds here until the onboarding has explained the verdict, rather
            // than moving on while the player is still reading about it
            practiceFeedbackRead = false;
            FeedbackShown?.Invoke(isCorrect);
            yield return new WaitUntil(() => practiceFeedbackRead);
        }
        else if (hasFeedback)
        {
            yield return new WaitForSecondsRealtime(feedbackReadSeconds);
        }

        currentQuestionIndex++;

        yield return new WaitForSecondsRealtime(postFeedbackDelay);

        if (answerBox != null)
            answerBox.ClearBox();

        minigameTimedOut = false;

        // Runs on the question itself, not the answer — right or wrong, the player still
        // pours the water and still blows the whistle. Uses the index of the question just
        // answered, since currentQuestionIndex has already moved on.
        if (waterMinigame != null && QuestionOwnsMinigame(answerBoxIndex, waterMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(waterMinigame.Play(), waterMinigame.ForceClose, waterMinigame));

        if (whistleMinigame != null && QuestionOwnsMinigame(answerBoxIndex, whistleMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(whistleMinigame.Play(), whistleMinigame.ForceClose, whistleMinigame));

        if (flashlightMinigame != null && QuestionOwnsMinigame(answerBoxIndex, flashlightMinigameItemNames))
            yield return StartCoroutine(PlayTimedMinigame(flashlightMinigame.Play(), flashlightMinigame.ForceClose, flashlightMinigame));

        if (glowstickMinigame != null && QuestionOwnsMinigame(answerBoxIndex, glowstickMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(glowstickMinigame.Play(), glowstickMinigame.ForceClose, glowstickMinigame));

        if (dustMaskMinigame != null && QuestionOwnsMinigame(answerBoxIndex, dustMaskMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(dustMaskMinigame.Play(), dustMaskMinigame.ForceClose, dustMaskMinigame));

        if (firstAidMinigame != null && QuestionOwnsMinigame(answerBoxIndex, firstAidMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(firstAidMinigame.Play(), firstAidMinigame.ForceClose, firstAidMinigame));

        if (pocketKnifeMinigame != null && QuestionOwnsMinigame(answerBoxIndex, pocketKnifeMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(pocketKnifeMinigame.Play(), pocketKnifeMinigame.ForceClose, pocketKnifeMinigame));

        if (ropeKnotMinigame != null && QuestionOwnsMinigame(answerBoxIndex, ropeKnotMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(ropeKnotMinigame.Play(), ropeKnotMinigame.ForceClose, ropeKnotMinigame));

        if (medicationMinigame != null && QuestionOwnsMinigame(answerBoxIndex, medicationMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(medicationMinigame.Play(), medicationMinigame.ForceClose, medicationMinigame));

        if (ziplockMinigame != null && QuestionOwnsMinigame(answerBoxIndex, ziplockMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(ziplockMinigame.Play(), ziplockMinigame.ForceClose, ziplockMinigame));

        if (thermalBlanketMinigame != null && QuestionOwnsMinigame(answerBoxIndex, thermalBlanketMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(thermalBlanketMinigame.Play(), thermalBlanketMinigame.ForceClose, thermalBlanketMinigame));

        if (radioMinigame != null && QuestionOwnsMinigame(answerBoxIndex, radioMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(radioMinigame.Play(), radioMinigame.ForceClose, radioMinigame));

        if (contactCardMinigame != null && QuestionOwnsMinigame(answerBoxIndex, contactCardMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(contactCardMinigame.Play(), contactCardMinigame.ForceClose, contactCardMinigame));

        if (cannedFoodMinigame != null && QuestionOwnsMinigame(answerBoxIndex, cannedFoodMinigameItemNames))
            yield return StartCoroutine(PlayTimedMinigame(cannedFoodMinigame.Play(), cannedFoodMinigame.ForceClose, cannedFoodMinigame));

        if (penAndPaperMinigame != null && QuestionOwnsMinigame(answerBoxIndex, penAndPaperMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(penAndPaperMinigame.Play(), penAndPaperMinigame.ForceClose, penAndPaperMinigame));

        if (importantDocumentsMinigame != null && QuestionOwnsMinigame(answerBoxIndex, importantDocumentsMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(importantDocumentsMinigame.Play(), importantDocumentsMinigame.ForceClose, importantDocumentsMinigame));

        if (batteriesMinigame != null && QuestionOwnsMinigame(answerBoxIndex, batteriesMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(batteriesMinigame.Play(), batteriesMinigame.ForceClose, batteriesMinigame));

        if (clothesMinigame != null && QuestionOwnsMinigame(answerBoxIndex, clothesMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(clothesMinigame.Play(), clothesMinigame.ForceClose, clothesMinigame));

        if (glovesMinigame != null && QuestionOwnsMinigame(answerBoxIndex, glovesMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(glovesMinigame.Play(), glovesMinigame.ForceClose, glovesMinigame));

        if (toiletriesMinigame != null && QuestionOwnsMinigame(answerBoxIndex, toiletriesMinigameItemName))
            yield return StartCoroutine(PlayTimedMinigame(toiletriesMinigame.Play(), toiletriesMinigame.ForceClose, toiletriesMinigame));

        // The practical half of this question is done — either its minigame has just played
        // through, or it never had one. Counted for right and wrong answers alike, but not
        // when the minigame timer ran out before the player finished it.
        if (!minigameTimedOut)
            tasksCompleted++;

        if (currentQuestionIndex >= TotalQuestions)
        {
            FinishQuiz();
            yield break;
        }

        ShowQuestion(currentQuestionIndex);
        isResolvingAnswer = false;
    }

    /// <summary>
    /// The line shown under the verdict for the question just answered, or empty if that
    /// question has none — most of them do not, and those show the verdict on its own.
    /// </summary>
    private string GetFeedback(int answerBoxIndex, bool isCorrect)
    {
        if (answerBoxIndex < 0 || answerBoxIndex >= randomizedQuestions.Count)
            return string.Empty;

        QuestionData q = randomizedQuestions[answerBoxIndex];
        string text = isCorrect ? q.correctFeedback : q.incorrectFeedback;

        return string.IsNullOrEmpty(text) ? string.Empty : text;
    }

    /// <summary>
    /// True when the question at <paramref name="questionIndex"/> is the one a minigame
    /// belongs to, named here by the item that answers it. Keyed off the question's answer
    /// item rather than a fixed index, because the questions are shuffled every run.
    /// </summary>
    private bool QuestionOwnsMinigame(int questionIndex, string minigameItemName)
    {
        return QuestionOwnsMinigame(questionIndex, new string[] { minigameItemName });
    }

    /// <summary>
    /// The several-answer form, for a question like the flashlight one where either item
    /// is correct. Matching any single name is enough.
    /// </summary>
    private bool QuestionOwnsMinigame(int questionIndex, string[] minigameItemNames)
    {
        if (minigameItemNames == null)
            return false;

        string[] correctAnswers = GetCorrectAnswersForAnswerBox(questionIndex);
        if (correctAnswers == null)
            return false;

        foreach (string wanted in minigameItemNames)
        {
            if (string.IsNullOrEmpty(wanted))
                continue;

            foreach (string answer in correctAnswers)
            {
                if (wanted.Equals(answer, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private void FinishQuiz()
    {
        StopQuestionTimer();

        if (questionTimerText != null)
            questionTimerText.gameObject.SetActive(false);

        if (questionTimerBar != null)
            questionTimerBar.Hide();

        if (dialogueBox != null)
            dialogueBox.SetActive(false);

        if (characterPortrait != null)
            characterPortrait.gameObject.SetActive(false);

        if (feedbackBanner != null)
            feedbackBanner.Hide();

        if (feedbackScrim != null)
            feedbackScrim.Clear();

        // Practice has no results screen and nothing to report: the onboarding takes it from here
        if (practiceQuiz)
        {
            isResolvingAnswer = false;
            PracticeQuizFinished?.Invoke();
            return;
        }

        StartCoroutine(HideInventoryAndShowResults(correctCount));
    }

    private IEnumerator HideInventoryAndShowResults(int score)
    {
        // Stop background music
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.StopMusic();
        }

        yield return null;

        if (inventoryPanel != null)
            inventoryPanel.SetActive(false);

        // Get results data
        GameTimer timerScript = FindFirstObjectByType<GameTimer>();
        float remainingTime = timerScript != null ? timerScript.GetTimeRemaining() : 0f;
        float totalTime = timerScript != null ? timerScript.GetTotalTime() : 0f;
        string difficulty = PlayerPrefs.GetString(DIFFICULTY_PREF, "beginner");

        DrillScore.Result drill = BuildDrillScore(score, remainingTime, totalTime);

        // Everything downstream reads the drill score from here, so the panels, the sound
        // and anything the teacher dashboard picks up all agree on one number.
        PlayerPrefs.SetInt("LastDrillScore", drill.FinalScore);
        PlayerPrefs.SetString("LastDrillBadge", drill.Badge);
        PlayerPrefs.Save();

        // A good enough score opens the next difficulty. Before the results panel, so its
        // NEXT DIFFICULTY button already knows.
        DifficultyProgress.RecordScore(difficulty, drill.FinalScore);

        // Teacher sessions report the run to the dashboard (offline practice does nothing here)
        SessionResultUploader.Submit(drill, score, TotalQuestions, remainingTime, totalTime, difficulty);

        // Show results panel
        if (resultsPanel != null)
        {
            resultsPanel.SetActive(true);
            ResultsPanel panelHandler = resultsPanel.GetComponent<ResultsPanel>();
            if (panelHandler != null)
            {
                panelHandler.DisplayResults(score, TotalQuestions, remainingTime, totalTime, difficulty, drill);
            }
        }

        // The cheer now follows the drill grade rather than the raw answer count, so it
        // matches the badge the player is being shown.
        SoundManager.Sfx(drill.FinalScore >= DrillScore.BADGE_PROFICIENT_MIN ? highScoreSFX : lowScoreSFX);

        isResolvingAnswer = false;
    }

    /// <summary>
    /// Gathers everything the 100-point drill score needs: what ended up in the bag, what
    /// the level offered to pack, the weight limit, how the quiz went and how much time was
    /// left. The arithmetic itself lives in <see cref="DrillScore"/>.
    /// </summary>
    private DrillScore.Result BuildDrillScore(int correct, float remainingTime, float totalTime)
    {
        return DrillScore.Compute(packedAtOpen, essentialTargetAtOpen, weightLimitAtOpen,
                                  TotalQuestions, correct, tasksCompleted,
                                  remainingTime, totalTime, timeParFraction);
    }

    /// <summary>
    /// Records what the player packed, before the quiz begins consuming correct answers out
    /// of the bag. Importance, weight and essentials come from each item's own SupplyItem.
    /// </summary>
    private void CaptureBagSnapshot()
    {
        packedAtOpen.Clear();
        weightLimitAtOpen = 0f;
        essentialTargetAtOpen = DrillScore.CountEssentialTarget(Resources.LoadAll<SupplyItem>("ItemData"));

        if (InventoryManager.Instance == null)
            return;

        weightLimitAtOpen = InventoryManager.Instance.GetGoBagWeightLimit();

        foreach (InventoryItem item in InventoryManager.Instance.GetGoBagItems())
        {
            if (item == null || string.IsNullOrEmpty(item.itemName))
                continue;

            if (item.source != null)
            {
                // Stackables count once per unit carried
                DrillScore.PackedItem known = new DrillScore.PackedItem(item.source);
                int units = Mathf.Max(1, item.quantity);
                for (int i = 0; i < units; i++)
                    packedAtOpen.Add(known);
            }
            else
            {
                // Something in the bag with no SupplyItem behind it: carry its weight so the
                // limit still bites, and count it as junk (it costs the junk penalty).
                packedAtOpen.Add(new DrillScore.PackedItem(
                    item.itemName, ItemImportance.Nuisance, item.weightKg));
            }
        }

        // Whatever made it into the bag is now registered in the Journal for the next game
        List<string> packedNames = new List<string>();
        foreach (DrillScore.PackedItem packed in packedAtOpen)
            packedNames.Add(packed.Name);
        JournalProgress.Unlock(packedNames);
    }

    /// <summary>
    /// True while an answer is being scored — the answer box refuses drops during this.
    /// </summary>
    public bool IsResolvingAnswer()
    {
        return isResolvingAnswer;
    }

    /// <summary>
    /// Get the question currently on screen.
    /// </summary>
    public QuestionData GetQuestionForAnswerBox(int answerBoxIndex)
    {
        if (answerBoxIndex >= 0 && answerBoxIndex < randomizedQuestions.Count)
        {
            return randomizedQuestions[answerBoxIndex];
        }
        return new QuestionData();
    }

    /// <summary>
    /// Get the correct answer item names for a specific question.
    /// </summary>
    public string[] GetCorrectAnswersForAnswerBox(int answerBoxIndex)
    {
        if (answerBoxIndex >= 0 && answerBoxIndex < randomizedQuestions.Count)
        {
            return randomizedQuestions[answerBoxIndex].correctAnswerItemNames;
        }
        return new string[] { };
    }

    /// <summary>
    /// Check if a given item name is a correct answer for a specific question.
    /// Supports multiple correct answers per question.
    /// </summary>
    public bool IsCorrectAnswer(int answerBoxIndex, string itemName)
    {
        if (answerBoxIndex >= 0 && answerBoxIndex < randomizedQuestions.Count)
        {
            string[] correctAnswers = randomizedQuestions[answerBoxIndex].correctAnswerItemNames;
            if (correctAnswers != null)
            {
                foreach (string correctAnswer in correctAnswers)
                {
                    if (correctAnswer.Equals(itemName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
