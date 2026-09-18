using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 100-point emergency preparedness drill score.
///
///   S_total = 0.40*S_pack + 0.45*S_quiz + 0.15*S_time
///
///   S_pack  = Max(0, (E_packed / E_target)*100 - (D_packed*10) - P_weight)
///   S_quiz  = (1/N) * Sum(0.60*C_match + 0.40*M_task) * 100
///   S_time  = gated on S_pack >= 60 AND S_quiz >= 60, else 0
///
/// Packing is scored against an authored list of essentials rather than against the
/// heaviest-value bag arithmetic could build. That matters: an optimiser maximising
/// importance-per-kilo throws the water bottle out, because a whistle is worth the same on
/// paper for a fraction of the weight. Grading against the items the quiz actually teaches
/// keeps the two halves of the game saying the same thing.
///
/// The maths lives here rather than in QuizManager so it can be checked on its own, without
/// a scene, a bag or a running quiz.
/// </summary>
public static class DrillScore
{
    public const float PACKING_WEIGHT = 40f;
    public const float QUIZ_WEIGHT = 45f;
    public const float TIME_WEIGHT = 15f;

    /// <summary>Split of a question's own score: naming the right item vs doing the task.</summary>
    public const float IDENTIFY_SHARE = 0.60f;
    public const float TASK_SHARE = 0.40f;

    /// <summary>Cost of each nuisance item carried, in S_pack points (D_packed * 10).</summary>
    public const int JUNK_PENALTY = 10;

    /// <summary>Cost of exceeding the weight limit, in S_pack points (P_weight).</summary>
    public const int OVERWEIGHT_PENALTY = 10;

    /// <summary>
    /// Anti-rush rule: finishing early earns nothing unless the bag and the quiz were both
    /// good enough. Sprinting out with an empty bag is not preparedness. Measured on S_pack
    /// after deductions, so a bag full of junk forfeits the time bonus too.
    /// </summary>
    public const float SPEED_GATE = 60f;

    /// <summary>
    /// Par time, as a fraction of the limit: finish within this and the time points are all
    /// yours, after which they taper to nothing at the limit itself.
    ///
    /// Scoring the clock straight — remaining over total — sounds right but quietly makes
    /// the last points unreachable, because full marks would need a zero-second run. A par
    /// gives the student something attainable to beat. Set to 0 for the straight fraction.
    /// </summary>
    public const float DEFAULT_TIME_PAR = 0.5f;

    public const int BADGE_MASTER_MIN = 88;
    public const int BADGE_PROFICIENT_MIN = 70;

    /// <summary>
    /// E_target. Each entry is one requirement, satisfied by any one of its listed items —
    /// so either torch counts, and either tin counts, without demanding both.
    ///
    /// This is deliberately the set the quiz teaches. Every correct answer in the question
    /// pool appears here, and nothing else does, so a student who learns the lessons packs
    /// a perfect bag — and is never asked to carry something the game never taught.
    ///
    /// Keep the two in step: dropping a question means dropping its item from here too.
    /// </summary>
    public static readonly string[][] DEFAULT_ESSENTIALS =
    {
        new[] { "Water Bottle" },
        new[] { "First Aid Kit" },
        new[] { "Small Flashlight", "Big Flashlight" },
        new[] { "Whistle" },
        new[] { "Canned Corned Beef", "Canned Fish" },
        new[] { "Dust Mask" },
        new[] { "Thermal Blanket" },
        new[] { "Medication" },
        new[] { "Spare Clothes" },
        new[] { "Rope" },
        new[] { "Ziplock Bag" },
        new[] { "Glow Sticks" },
        new[] { "Pocket Knife" }
    };

    public struct Result
    {
        public float PackingPercent;    // S_pack, 0-1 (after deductions)
        public float QuizPercent;       // S_quiz, 0-1
        public float TimePercent;       // S_time, 0-1

        public float PackingPoints;     // of 40
        public float QuizPoints;        // of 45
        public float TimePoints;        // of 15

        public int EssentialsPacked;    // E_packed
        public int EssentialsTarget;    // E_target
        public int JunkCount;           // D_packed
        public bool OverWeight;
        public int Deductions;          // points lost inside S_pack
        public bool SpeedGatePassed;

        public int FinalScore;          // 0-100
        public string Badge;
        public string BadgeMeaning;
    }

    /// <summary>One packed item, reduced to just what scoring cares about.</summary>
    public struct PackedItem
    {
        public string Name;
        public ItemImportance Importance;
        public float WeightKg;

        public PackedItem(string name, ItemImportance importance, float weightKg)
        {
            Name = name;
            Importance = importance;
            WeightKg = weightKg;
        }
    }

    /// <summary>
    /// How many of the required groups the bag satisfies, and how many there were.
    /// </summary>
    public static void CountEssentials(IList<PackedItem> packed, string[][] essentials,
                                       out int found, out int target)
    {
        if (essentials == null)
            essentials = DEFAULT_ESSENTIALS;

        target = essentials.Length;
        found = 0;

        HashSet<string> have = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        if (packed != null)
        {
            foreach (PackedItem item in packed)
                if (!string.IsNullOrEmpty(item.Name))
                    have.Add(item.Name);
        }

        foreach (string[] group in essentials)
        {
            if (group == null)
                continue;

            foreach (string option in group)
            {
                if (have.Contains(option))
                {
                    found++;
                    break; // any one of the alternates satisfies the requirement
                }
            }
        }
    }

    /// <summary>
    /// Scores a finished drill.
    /// </summary>
    /// <param name="packed">What ended up in the go bag.</param>
    /// <param name="weightLimitKg">The difficulty's limit. 0 means unlimited.</param>
    /// <param name="questionsAsked">N_quiz — length of the round.</param>
    /// <param name="correctAnswers">Sum of C_match.</param>
    /// <param name="tasksCompleted">
    /// Sum of M_task. A question with no minigame counts as complete: there is nothing to
    /// fail, and the component scores taking part rather than getting the answer right.
    /// </param>
    public static Result Compute(
        IList<PackedItem> packed,
        float weightLimitKg,
        int questionsAsked,
        int correctAnswers,
        int tasksCompleted,
        float timeRemaining,
        float timeTotal,
        float timeParFraction = DEFAULT_TIME_PAR,
        string[][] essentials = null)
    {
        Result r = new Result();

        // ---- 1. S_pack ----
        int found, target;
        CountEssentials(packed, essentials, out found, out target);
        r.EssentialsPacked = found;
        r.EssentialsTarget = target;

        float carried = 0f;
        int junk = 0;
        if (packed != null)
        {
            foreach (PackedItem item in packed)
            {
                carried += item.WeightKg;
                if (item.Importance == ItemImportance.Nuisance)
                    junk++;
            }
        }

        r.JunkCount = junk;
        // A hair of tolerance: the limit is a design figure, not a float-equality test
        r.OverWeight = weightLimitKg > 0f && carried > weightLimitKg + 0.0001f;
        r.Deductions = junk * JUNK_PENALTY + (r.OverWeight ? OVERWEIGHT_PENALTY : 0);

        float coverage = target > 0 ? (found / (float)target) * 100f : 0f;
        float sPack = Mathf.Max(0f, coverage - r.Deductions);
        r.PackingPercent = sPack / 100f;
        r.PackingPoints = r.PackingPercent * PACKING_WEIGHT;

        // ---- 2. S_quiz ----
        if (questionsAsked > 0)
        {
            float identify = Mathf.Clamp01(correctAnswers / (float)questionsAsked);
            float task = Mathf.Clamp01(tasksCompleted / (float)questionsAsked);
            r.QuizPercent = identify * IDENTIFY_SHARE + task * TASK_SHARE;
        }
        r.QuizPoints = r.QuizPercent * QUIZ_WEIGHT;

        // ---- 3. S_time ----
        float used = timeTotal > 0f ? Mathf.Clamp01(1f - (timeRemaining / timeTotal)) : 1f;
        float par = Mathf.Clamp(timeParFraction, 0f, 0.99f);
        float promptness = used <= par ? 1f : Mathf.Clamp01((1f - used) / (1f - par));

        r.SpeedGatePassed = sPack >= SPEED_GATE && (r.QuizPercent * 100f) >= SPEED_GATE;
        r.TimePercent = r.SpeedGatePassed ? promptness : 0f;
        r.TimePoints = r.TimePercent * TIME_WEIGHT;

        // ---- total ----
        r.FinalScore = Mathf.Clamp(
            Mathf.RoundToInt(r.PackingPoints + r.QuizPoints + r.TimePoints), 0, 100);

        AssignBadge(ref r);
        return r;
    }

    private static void AssignBadge(ref Result r)
    {
        if (r.FinalScore >= BADGE_MASTER_MIN)
        {
            r.Badge = "Autonomous (Master)";
            r.BadgeMeaning = "Intuitive & Flawless: Rapid item selection with zero distractors " +
                             "and swift emergency response. Ready for real evacuation.";
        }
        else if (r.FinalScore >= BADGE_PROFICIENT_MIN)
        {
            r.Badge = "Associative (Proficient)";
            r.BadgeMeaning = "Good Competence: Understands survival priorities (water and " +
                             "medical first), makes only minor errors, and demonstrates safe " +
                             "decision-making.";
        }
        else
        {
            r.Badge = "Cognitive (Needs Support)";
            r.BadgeMeaning = "Needs Practice: Hesitant item selection, brought unnecessary " +
                             "weight, or missed key scenarios. Dashboard alerts teacher for " +
                             "remediation.";
        }
    }
}
