// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class SpeedEvaluator
    {
        private const double single_spacing_threshold = 150;
        private const double min_speed_bonus = 75; // ~200BPM
        private const double speed_balancing_factor = 40;
        private const double reaction_time = 150;

        /// <summary>
        /// Evaluates the difficulty of tapping the current object, based on:
        /// <list type="bullet">
        /// <item><description>time between pressing the previous and current object,</description></item>
        /// <item><description>distance between those objects,</description></item>
        /// <item><description>and how easily they can be cheesed.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current)
        {
            if (current.BaseObject is Spinner)
                return 0;

            // derive strainTime for calculation
            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuPrevObj = current.Index > 0 ? (OsuDifficultyHitObject)current.Previous(0) : null;
            var osuNextObj = (OsuDifficultyHitObject?)current.Next(0);

            // high AR buff
            double arBuff = 1.0;

            // follow lines make high AR easier, so apply nerf if object isn't new combo
            double adjustedApproachTime = osuCurrObj.ApproachRateTime + Math.Max(0, (osuCurrObj.FollowLineTime - 200) / 25);

            // we are assuming that 150ms is a complete memory and the bonus will be maximal (1.5) on this
            if (adjustedApproachTime < 150)
                arBuff += 0.4;

            // bonus for AR starts from AR10.3, balancing bonus based on high SR cuz we don't have density calculation
            if (adjustedApproachTime < 400)
            {
                arBuff += 0.2 * (1 + Math.Cos(Math.PI * 0.4 * (adjustedApproachTime - 150) / 100));
            }

            //double arBuff = (1.0 + 0.05 * Math.Max(0.0, 400.0 - osuCurrObj.ApproachRateTime) / 100.0);

            double strainTime = osuCurrObj.StrainTime;
            double readingTime = osuCurrObj.StrainTime;
            if (osuPrevObj != null)
            {
                strainTime = (osuCurrObj.StrainTime + osuPrevObj.StrainTime) / 2;
                readingTime = Math.Min(Math.Max(speed_balancing_factor, osuPrevObj.MovementTime + osuCurrObj.StrainTime) / 2, 
                                       osuCurrObj.ApproachRateTime - reaction_time);
            }

            double doubletapness = 1;

            // Nerf doubletappable doubles.
            if (osuNextObj != null)
            {
                double currDeltaTime = Math.Max(1, osuCurrObj.DeltaTime);
                double nextDeltaTime = Math.Max(1, osuNextObj.DeltaTime);
                double deltaDifference = Math.Abs(nextDeltaTime - currDeltaTime);
                double speedRatio = currDeltaTime / Math.Max(currDeltaTime, deltaDifference);
                double windowRatio = Math.Pow(Math.Min(1, currDeltaTime / osuCurrObj.HitWindowGreat), 2);
                doubletapness = Math.Pow(speedRatio, 1 - windowRatio);
            }

            return arBuff * doubletapness * (1 / (readingTime - 20));
        }
    }
}
