// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class RhythmEvaluator
    {
        private const int history_time_max = 5000; // 5 seconds of calculatingRhythmBonus max.
        private const double rhythm_multiplier = 1.5;

        /// <summary>
        /// Calculates a rhythm multiplier for the difficulty of the tap associated with historic data of the current <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            int previousIslandSize = 0;

            double rhythmComplexitySum = 0;
            int islandSize = 1;
            double startRatio = 0; // store the ratio of the current start of an island to buff for tighter rhythms

            bool firstDeltaSwitch = false;

            int historicalNoteCount = Math.Min(current.Index, 32);

            int rhythmStart = 0;

            while (rhythmStart < historicalNoteCount - 2 && current.StartTime - current.Previous(rhythmStart).StartTime < history_time_max)
                rhythmStart++;

            for (int i = rhythmStart; i > 0; i--)
            {
                OsuDifficultyHitObject currObj = (OsuDifficultyHitObject)current.Previous(i - 1);
                OsuDifficultyHitObject prevObj = (OsuDifficultyHitObject)current.Previous(i);
                OsuDifficultyHitObject lastObj = (OsuDifficultyHitObject)current.Previous(i + 1);

                double currHistoricalDecay = (history_time_max - (current.StartTime - currObj.StartTime)) / history_time_max; // scales note 0 to 1 from history to now

                // currHistoricalDecay = Math.Min((double)(historicalNoteCount - i) / historicalNoteCount, currHistoricalDecay); // either we're limited by time or limited by object count.

                double currDelta = currObj.StrainTime;
                double prevDelta = prevObj.StrainTime;
                double lastDelta = lastObj.StrainTime;
                double currRatio = Math.PI * Math.Min(5, Math.Max(prevDelta, currDelta) / Math.Min(prevDelta, currDelta));
                currRatio = 1.0 + 8 * (-0.25 + Math.Min(0.75, Math.Max(0.25, Math.Pow(Math.Sin(currRatio), 2.0) + 0.2 * Math.Pow(Math.Sin(1.5 * currRatio), 2.0))));

                double windowPenalty = Math.Min(1, Math.Max(0, Math.Abs(prevDelta - currDelta) - currObj.HitWindowGreat * 0.3) / (currObj.HitWindowGreat * 0.3));

                windowPenalty = Math.Min(1, windowPenalty);

                double effectiveRatio = windowPenalty * currRatio;

                if (firstDeltaSwitch)
                {
                    if (!(prevDelta > 1.1 * currDelta || prevDelta * 1.1 < currDelta))
                    {
                        if (islandSize < 6)
                            islandSize++; // island is still progressing, count size.
                    }
                    else
                    {
                        islandSize++;

                        if (lastDelta > prevDelta + 10 && prevDelta > currDelta + 10) // previous increase happened a note ago, 1/1->1/2-1/4, dont want to buff this.
                            effectiveRatio *= 0.125;

                        effectiveRatio = applyPenalties(effectiveRatio, current.Previous(i), current.Previous(i - 1), islandSize, previousIslandSize);

                        rhythmComplexitySum += Math.Sqrt(effectiveRatio * startRatio) * currHistoricalDecay;

                        startRatio = applyPenalties(effectiveRatio, current.Previous(i), current.Previous(i - 1), islandSize, previousIslandSize);;

                        previousIslandSize = islandSize; // log the last island size.

                        if (prevDelta * 1.1 < currDelta) // we're slowing down, stop counting
                            firstDeltaSwitch = false; // if we're speeding up, this stays true and  we keep counting island size.

                        islandSize = 1;
                    }
                }
                else if (prevDelta > 1.1 * currDelta) // we want to be speeding up.
                {
                    // Begin counting island until we change speed again.
                    firstDeltaSwitch = true;
                    startRatio = applyPenalties(effectiveRatio, current.Previous(i), current.Previous(i - 1), islandSize, previousIslandSize);
                    islandSize = 1;
                }
            }
            
            return Math.Sqrt(4 + rhythmComplexitySum * rhythm_multiplier) / 2; //produces multiplier that can be applied to strain. range [1, infinity) (not really though)
        }

        private static double applyPenalties(double effectiveRatio, DifficultyHitObject prev, DifficultyHitObject curr, int islandSize, int previousIslandSize)
        {
            if (prev.BaseObject is Slider) // bpm change is into slider, this is easy acc window
                effectiveRatio *= 0.125;

            if (curr.BaseObject is Slider) // bpm change was from a slider, this is easier typically than circle -> circle
                effectiveRatio *= 0.125;

            if (previousIslandSize == islandSize) // repeated island size (ex: triplet -> triplet)
                effectiveRatio *= 0.5;

            if (previousIslandSize % 2 == islandSize % 2) // repeated island polartiy (2 -> 4, 3 -> 5)
                effectiveRatio *= 0.5;

            if (islandSize % 2 == 0 && islandSize != 2)
                effectiveRatio *= 2;

            return effectiveRatio;
        }
    }
}
