using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The 100-point emergency preparedness drill score.
///
///   S_total = 0.40*S_pack + 0.20*S_quiz + 0.20*S_task + 0.20*S_time
///
///   S_pack  = Max(0, (E_packed / E_target)*100 - (D_packed*10))
///   S_quiz  = (C_match / N) * 100          — answering with the right item from the bag
///   S_task  = (M_task  / N) * 100          — seeing the minigame through
///   S_time  = gated on S_pack >= 60 AND the quiz side >= 60, else 0
///
/// S_quiz is not independent of S_pack, by design. A question is answered by dragging an
/// item out of the go bag, so an essential left behind can't be given as an answer either:
/// forgetting the water bottle costs its packing share and, if its question comes up, that
/// question too. In an emergency you only have what you packed.
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

    /// <summary>Answering the scenario with the right item out of the bag.</summary>
    public const float QUIZ_WEIGHT = 20f;

    /// <summary>Seeing the scenario's minigame through before its clock runs out.</summary>
    public const float TASK_WEIGHT = 20f;

    public const float TIME_WEIGHT = 20f;

    /// <summary>Cost of each nuisance item carried, in S_pack points (D_packed * 10).</summary>
    public const int JUNK_PENALTY = 10;

    /// <summary>
    /// Anti-rush rule: finishing early earns nothing unless the bag and the quiz were both
    /// good enough. Sprinting out with an empty bag is not preparedness. Measured on S_pack
    /// after deductions, so a bag full of junk forfeits the time bonus too, and on the quiz
    /// and minigame halves together, so neither alone can open the gate.
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

    // E_target is set on the item data, as each SupplyItem's Essential Rank: every rank is
    // one requirement, satisfied by any one of the items that share it - so either torch
    // counts, and either tin counts, without demanding both.
    //
    // This is deliberately the set the quiz teaches. Every correct answer in the question
    // pool is an essential, and nothing else is, so a student who learns the lessons packs a
    // perfect bag - and is never asked to carry something the game never taught. Keep the two
    // in step: dropping a question means clearing its item's Essential Rank too.

    public struct Result
    {
        public float PackingPercent;    // S_pack, 0-1 (after deductions)
        public float QuizPercent;       // S_quiz, 0-1 — right items given from the bag
        public float TaskPercent;       // S_task, 0-1 — minigames seen through
        public float TimePercent;       // S_time, 0-1

        public float PackingPoints;     // of 40
        public float QuizPoints;        // of 20
        public float TaskPoints;        // of 20
        public float TimePoints;        // of 20

        public int EssentialsPacked;    // E_packed
        public int EssentialsTarget;    // E_target
        public int JunkCount;           // D_packed
        public bool OverWeight;
        public int Deductions;          // points lost inside S_pack
        public bool SpeedGatePassed;

        public int FinalScore;          // 0-100
        public string Badge;            // set by FinalScore alone
    }

    /// <summary>One packed item, reduced to just what scoring cares about.</summary>
    public struct PackedItem
    {
        public string Name;
        public ItemImportance Importance;
        public float WeightKg;
        public int EssentialRank;  // 0 = not an essential

        public PackedItem(string name, ItemImportance importance, float weightKg, int essentialRank = 0)
        {
            Name = name;
            Importance = importance;
            WeightKg = weightKg;
            EssentialRank = essentialRank;
        }

        public PackedItem(SupplyItem supply)
            : this(supply.ItemName, supply.Importance, supply.WeightKg, supply.EssentialRank)
        {
        }
    }

    /// <summary>E_target: how many essential requirements the item data defines.</summary>
    public static int CountEssentialTarget(IEnumerable<SupplyItem> allItems)
    {
        HashSet<int> ranks = new HashSet<int>();
        if (allItems != null)
        {
            foreach (SupplyItem item in allItems)
                if (item != null && item.IsEssential)
                    ranks.Add(item.EssentialRank);
        }
        return ranks.Count;
    }

    /// <summary>E_packed: how many different essential requirements the bag covers.</summary>
    public static int CountEssentialsPacked(IList<PackedItem> packed)
    {
        HashSet<int> covered = new HashSet<int>();
        if (packed != null)
        {
            foreach (PackedItem item in packed)
                if (item.EssentialRank > 0)
                    covered.Add(item.EssentialRank);  // any one of the alternates covers it
        }
        return covered.Count;
    }

    /// <summary>
    /// Scores a finished drill.
    /// </summary>
    /// <param name="packed">What ended up in the go bag.</param>
    /// <param name="essentialTarget">E_target, from <see cref="CountEssentialTarget"/>.</param>
    /// <param name="weightLimitKg">The difficulty's limit. 0 means unlimited.</param>
    /// <param name="questionsAsked">N_quiz — length of the round.</param>
    /// <param name="correctAnswers">Sum of C_match.</param>
    /// <param name="tasksCompleted">
    /// Sum of M_task. A question with no minigame counts as complete: there is nothing to
    /// fail, and the component scores taking part rather than getting the answer right.
    /// </param>
    public static Result Compute(
        IList<PackedItem> packed,
        int essentialTarget,
        float weightLimitKg,
        int questionsAsked,
        int correctAnswers,
        int tasksCompleted,
        float timeRemaining,
        float timeTotal,
        float timeParFraction = DEFAULT_TIME_PAR)
    {
        Result r = new Result();

        // ---- 1. S_pack ----
        int target = essentialTarget;
        int found = Mathf.Min(CountEssentialsPacked(packed), target);
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
        // Reported for the dashboards, but not deducted: the bag itself refuses anything
        // that would push it past the limit, so this can only happen if that check is
        // bypassed. A hair of tolerance, since the limit is a design figure rather than a
        // float-equality test.
        r.OverWeight = weightLimitKg > 0f && carried > weightLimitKg + 0.0001f;
        r.Deductions = junk * JUNK_PENALTY;

        float coverage = target > 0 ? (found / (float)target) * 100f : 0f;
        float sPack = Mathf.Max(0f, coverage - r.Deductions);
        r.PackingPercent = sPack / 100f;
        r.PackingPoints = r.PackingPercent * PACKING_WEIGHT;

        // ---- 2. S_quiz and S_task ----
        // Scored apart rather than blended: naming the right item and doing the practical
        // task are two different things to learn, and the results panel shows each on its own.
        if (questionsAsked > 0)
        {
            r.QuizPercent = Mathf.Clamp01(correctAnswers / (float)questionsAsked);
            r.TaskPercent = Mathf.Clamp01(tasksCompleted / (float)questionsAsked);
        }
        r.QuizPoints = r.QuizPercent * QUIZ_WEIGHT;
        r.TaskPoints = r.TaskPercent * TASK_WEIGHT;

        // ---- 3. S_time ----
        float used = timeTotal > 0f ? Mathf.Clamp01(1f - (timeRemaining / timeTotal)) : 1f;
        float par = Mathf.Clamp(timeParFraction, 0f, 0.99f);
        float promptness = used <= par ? 1f : Mathf.Clamp01((1f - used) / (1f - par));

        float quizSide = (r.QuizPoints + r.TaskPoints) / (QUIZ_WEIGHT + TASK_WEIGHT) * 100f;
        r.SpeedGatePassed = sPack >= SPEED_GATE && quizSide >= SPEED_GATE;
        r.TimePercent = r.SpeedGatePassed ? promptness : 0f;
        r.TimePoints = r.TimePercent * TIME_WEIGHT;

        // ---- total ----
        r.FinalScore = Mathf.Clamp(
            Mathf.RoundToInt(r.PackingPoints + r.QuizPoints + r.TaskPoints + r.TimePoints), 0, 100);

        AssignBadge(ref r);
        return r;
    }

    /// <summary>
    /// The badge is a score band and nothing more. It deliberately carries no description of
    /// what the student did: the old ones promised things the score never checked (a Master
    /// could carry junk, and every essential counts the same however urgent it is).
    /// </summary>
    private static void AssignBadge(ref Result r)
    {
        if (r.FinalScore >= BADGE_MASTER_MIN)
            r.Badge = "Autonomous (Master)";
        else if (r.FinalScore >= BADGE_PROFICIENT_MIN)
            r.Badge = "Associative (Proficient)";
        else
            r.Badge = "Cognitive (Needs Support)";
    }

    /// <summary>
    /// The weight of the lightest bag that covers every essential: the lightest item of each
    /// rank, added up. If this is over the weight limit, a perfect packing score can't be
    /// reached. Checked in the editor by EssentialWeightCheck.
    /// </summary>
    public static float LightestFullSetKg(IEnumerable<SupplyItem> allItems)
    {
        Dictionary<int, float> lightest = new Dictionary<int, float>();
        if (allItems != null)
        {
            foreach (SupplyItem item in allItems)
            {
                if (item == null || !item.IsEssential)
                    continue;

                if (!lightest.TryGetValue(item.EssentialRank, out float kg) || item.WeightKg < kg)
                    lightest[item.EssentialRank] = item.WeightKg;
            }
        }

        float total = 0f;
        foreach (float kg in lightest.Values)
            total += kg;
        return total;
    }
}
