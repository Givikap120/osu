// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators
{
    public static class AimEvaluator
    {
        private const double wide_angle_multiplier = 1.0;
        private const double acute_angle_multiplier = 1.95;
        private const double slider_multiplier = 1.35;
        private const double velocity_change_multiplier = 0.75;
        private const double reaction_time = 150;

        /// <summary>
        /// Evaluates the difficulty of aiming the current object, based on:
        /// <list type="bullet">
        /// <item><description>cursor velocity to the current object,</description></item>
        /// <item><description>angle difficulty,</description></item>
        /// <item><description>sharp velocity increases,</description></item>
        /// <item><description>and slider difficulty.</description></item>
        /// </list>
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, double strainDecayBase, double currentRhythm)
        {
            if (current.Index <= 2 || 
                current.BaseObject is Spinner || 
                current.Previous(0).BaseObject is Spinner ||
                current.Previous(1).BaseObject is Spinner ||
                current.Previous(2).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj0 = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastObj1 = (OsuDifficultyHitObject)current.Previous(1);
            var osuLastObj2 = (OsuDifficultyHitObject)current.Previous(2);

            double aimStrain = 0;

            //////////////////////// CIRCLE SIZE /////////////////////////
            double linearDifficulty = 32.0 / osuCurrObj.Radius;

            // Flow Stuff
            double flowDifficulty = linearDifficulty * osuCurrObj.Movement.Length / osuCurrObj.StrainTime;

            // Nerf flow aim where circles overlap. Aim requirement is significantly lower in these cases.
            flowDifficulty *= Math.Min(10, osuCurrObj.Movement.Length / (osuCurrObj.Radius * 2));

            // Snap Stuff
            // Reduce strain time by 25ms to account for stopping time.
            double snapDifficulty = linearDifficulty * (osuCurrObj.Movement.Length / (osuCurrObj.StrainTime - 20) + (osuCurrObj.Radius * 2) / (Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime) - 20));

            // Arbitrary buff for high bpm snap because its hard.
            snapDifficulty *= Math.Sqrt(Math.Max(1, 100 / osuCurrObj.StrainTime));

            // Begin angle and weird rewards.
            double currVelocity = osuCurrObj.Movement.Length / osuCurrObj.StrainTime;
            double prevVelocity = osuLastObj0.Movement.Length / osuLastObj0.StrainTime;

            // Used to penalize additions if there is a change in the rhythm. Possible place to rework.
            double rhythmRatio = Math.Min(osuCurrObj.StrainTime, osuLastObj0.StrainTime) / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime);

            if (osuCurrObj.Angle != null && osuLastObj0.Angle != null)
            {
                double currAngle = osuCurrObj.Angle.Value;
                double lastAngle = osuLastObj0.Angle.Value;

                // We reward wide angles on snap.
                snapDifficulty += linearDifficulty * calculateAngleSpline(currAngle, false) * Math.Min(Math.Min(currVelocity, prevVelocity), (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime));

                // We reward for angle changes or the acuteness of the angle, whichever is higher. Possibly a case out there to reward both.
                flowDifficulty += Math.Max(linearDifficulty * calculateAngleSpline(Math.PI / 4 + Math.Min(Math.PI / 2, Math.Abs(lastAngle - currAngle)), false) * Math.Min(Math.Min(currVelocity, prevVelocity), (osuCurrObj.Movement + osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)),
                                           linearDifficulty * calculateAngleSpline(currAngle, true) * Math.Min(Math.Min(currVelocity, prevVelocity), (osuCurrObj.Movement - osuLastObj0.Movement).Length / Math.Max(osuCurrObj.StrainTime, osuLastObj0.StrainTime)));
            }

            flowDifficulty += linearDifficulty * Math.Abs(currVelocity - prevVelocity) * rhythmRatio;
            snapDifficulty += linearDifficulty * Math.Max(0, Math.Min(Math.Abs(currVelocity - prevVelocity) - Math.Min(currVelocity, prevVelocity), Math.Min(currVelocity, prevVelocity))) * rhythmRatio;

            // Apply balancing parameters.
            flowDifficulty = flowDifficulty * 1.25;
            snapDifficulty = snapDifficulty * 0.9;

            // Used in an LP sum to buff ambiguous snap flow scenarios.
            double p = 4.0;
            double minStrain = Math.Min(snapDifficulty, flowDifficulty);

            aimStrain = Math.Pow(Math.Pow(Math.Max(0, minStrain - Math.Abs(snapDifficulty - minStrain)), p) + Math.Pow(Math.Max(0, minStrain - Math.Abs(flowDifficulty - minStrain)), p), 1.0 / p);

            // aimStrain = minStrain;

            // Buff cases where the holding of a slider makes the subsequent jump harder, even with leniency abuse.
            aimStrain = Math.Max(aimStrain, (aimStrain - linearDifficulty * 2.4 * osuCurrObj.Radius / Math.Min(osuCurrObj.MovementTime, osuLastObj0.MovementTime)) * (osuCurrObj.StrainTime / osuCurrObj.MovementTime));   
        
            // Apply small CS buff.
            aimStrain *= Math.Sqrt(linearDifficulty);

            // Arbitrary cap to bonuses because balancing is hard.
            aimStrain = Math.Min(aimStrain, linearDifficulty * currVelocity * 3.25);

            // Slider stuff.
            double sustainedSliderStrain = 0.0;

            if (osuCurrObj.SliderSubObjects.Count != 0 && withSliderTravelDistance)
                sustainedSliderStrain = calculateSustainedSliderStrain(osuCurrObj, strainDecayBase, withSliderTravelDistance);
            
            // Apply slider strain with constant adjustment
            aimStrain += 1.5 * sustainedSliderStrain;

            // AR buff for aim.
            double arBuff = (1.0 + 0.05 * Math.Max(0.0, 400.0 - osuCurrObj.ApproachRateTime) / 100.0);

            return arBuff * aimStrain;
        }

        private static double calculateSustainedSliderStrain(OsuDifficultyHitObject osuCurrObj, double strainDecayBase, bool withSliderTravelDistance)
        {
            int index = 0;

            double sliderRadius = 2.4 * osuCurrObj.Radius;
            double linearDifficulty = 32.0 / osuCurrObj.Radius;

            var historyVector = new Vector2(0,0);
            double historyTime = 0;
            double historyDistance = 0;

            double peakStrain = 0;
            double currentStrain = 0;

            foreach (var subObject in osuCurrObj.SliderSubObjects)
            {
                if (index == osuCurrObj.SliderSubObjects.Count && !withSliderTravelDistance)
                    break;

                double noteStrain = 0;

                if (index == 0 && osuCurrObj.SliderSubObjects.Count > 1)
                    noteStrain = Math.Max(0, linearDifficulty * subObject.Movement.Length) / subObject.StrainTime;

                historyVector += subObject.Movement;
                historyTime += subObject.StrainTime;
                historyDistance += subObject.Movement.Length;
                
                if (historyVector.Length > sliderRadius * 2.0)
                {
                    noteStrain += linearDifficulty * historyDistance / historyTime;

                    historyVector = new Vector2(0,0);
                    historyTime = 0;
                    historyDistance = 0;
                }

                currentStrain *= Math.Pow(strainDecayBase, subObject.StrainTime / 1000.0); // TODO bug here using strainTime.
                currentStrain += noteStrain;

                index += 1;
            }

            if (historyTime > 0 && withSliderTravelDistance)
            {
                if (osuCurrObj.SliderSubObjects.Count > 1)
                    currentStrain += Math.Max(0, linearDifficulty * Math.Max(0, historyVector.Length) / historyTime);
                else
                    currentStrain += Math.Max(0, linearDifficulty * Math.Max(0, historyVector.Length - 2 * osuCurrObj.Radius) / historyTime);
            }

            return Math.Max(currentStrain, peakStrain);
        }

        private static double calculateAngleSpline(double angle, bool reversed)
        {
            angle = Math.Abs(angle);
            if (reversed)
                return 1 - Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 3.0, 5 * Math.PI / 6.0) - Math.PI / 3), 2.0);

            // return Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 6, 2 * Math.PI / 3.0) - Math.PI / 6), 2.0);

            // angle = Math.Abs(angle);
            // if (reversed)
            //     return 1 - Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 4.0, 3 * Math.PI / 4.0) - Math.PI / 4), 2.0);

            return Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 4.0, 3 * Math.PI / 4.0) - Math.PI / 4), 2.0);


            // angle = Math.Abs(angle);
            // if (reversed)
            //     return 1 - Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 3.0, 5 * Math.PI / 6.0) - Math.PI / 3), 2.0);

            // return Math.Pow(Math.Sin(Math.Clamp(angle, Math.PI / 3.0, 5 * Math.PI / 6.0) - Math.PI / 3), 2.0);
        }
    }
}
