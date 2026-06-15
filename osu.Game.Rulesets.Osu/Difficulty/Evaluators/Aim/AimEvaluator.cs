// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class AimEvaluator
    {
        private const double angle_bonus_begin = Math.PI / 3;
        private const double timing_threshold = 107;

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>angle difficulty,</description></item>
        /// <item><description>sharp velocity increases,</description></item>
        /// <item><description>and slider difficulty.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool _ = false)
        {
            if (current.BaseObject is Spinner)
                return 0;

            var osuCurrent = (OsuDifficultyHitObject)current;

            double result = 0;

            if (current.Index > 0)
            {
                var osuPrevious = (OsuDifficultyHitObject)current.Previous(0);

                if (osuCurrent.Angle != null && osuCurrent.Angle.Value > angle_bonus_begin)
                {
                    const double scale = 90;

                    double angleBonus = Math.Sqrt(
                        Math.Max(osuPrevious.JumpDistance - scale, 0)
                        * Math.Pow(Math.Sin(osuCurrent.Angle.Value - angle_bonus_begin), 2)
                        * Math.Max(osuCurrent.JumpDistance - scale, 0));
                    result = 1.4 * applyDiminishingExp(Math.Max(0, angleBonus)) / Math.Max(timing_threshold, osuPrevious.StrainTime);
                }
            }

            double jumpDistanceExp = applyDiminishingExp(osuCurrent.JumpDistance);
            double travelDistanceExp = applyDiminishingExp(osuCurrent.TravelDistance);

            return Math.Max(
                result + (jumpDistanceExp + travelDistanceExp + Math.Sqrt(travelDistanceExp * jumpDistanceExp)) / Math.Max(osuCurrent.StrainTime, timing_threshold),
                (Math.Sqrt(travelDistanceExp * jumpDistanceExp) + jumpDistanceExp + travelDistanceExp) / osuCurrent.StrainTime
            );
        }

        private static double applyDiminishingExp(double val) => Math.Pow(val, 0.99);
    }
}
