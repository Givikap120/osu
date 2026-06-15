// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultyHitObject : DifficultyHitObject
    {
        private const int normalized_radius = 52;

        protected new OsuHitObject BaseObject => (OsuHitObject)base.BaseObject;
        protected new OsuHitObject LastObject => (OsuHitObject)base.LastObject;

        /// <summary>
        /// Normalized distance from the <see cref="OsuHitObject.StackedPosition"/> of the previous <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double Distance { get; private set; }

        private double minDeltaTime => OsuPerformanceCalculator.UseCurrentBpmCap ? 25 : 40;

        public OsuDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            setDistances();
            DeltaTime = Math.Max(minDeltaTime, DeltaTime);
        }

        private void setDistances()
        {
            // We will scale distances by this factor, so we can assume a uniform CircleSize among beatmaps.
            double scalingFactor = normalized_radius / BaseObject.Radius;
            if (BaseObject.Radius < 30)
            {
                double smallCircleBonus = (30 - BaseObject.Radius) / 40;
                scalingFactor *= 1 + smallCircleBonus;
            }

            Vector2 lastCursorPosition = LastObject.Position;
            float lastTravelDistance = 0;

            if (LastObject is Slider lastSlider)
            {
                computeSliderCursorPosition(lastSlider);
                lastCursorPosition = lastSlider.LazyEndPosition ?? lastCursorPosition;
                lastTravelDistance = lastSlider.LazyTravelDistance;
            }

            Distance = (lastTravelDistance + (BaseObject.Position - lastCursorPosition).Length) * scalingFactor;
        }

        private void computeSliderCursorPosition(Slider slider)
        {
            if (slider.LazyEndPosition != null)
                return;
            slider.LazyEndPosition = slider.StackedPosition;

            float approxFollowCircleRadius = (float)(slider.Radius * 3);

            foreach (var nestedHitObject in slider.NestedHitObjects)
            {
                double progress = (nestedHitObject.StartTime - slider.StartTime) / slider.Duration;
                var diff = slider.Position + slider.CurvePositionAt(progress) - slider.LazyEndPosition.Value;
                float dist = diff.Length;

                if (dist > approxFollowCircleRadius)
                {
                    // The cursor would be outside the follow circle, we need to move it
                    diff.Normalize(); // Obtain direction of diff
                    dist -= approxFollowCircleRadius;
                    slider.LazyEndPosition += diff * dist;
                    slider.LazyTravelDistance += dist;
                }
            }
        }

        // To avoid compile errors with osu-tools
        public double AdjustedDeltaTime => DeltaTime;
        public double LazyJumpDistance => Distance;
        public double MinimumJumpDistance => Distance;
        public double MinimumJumpTime => DeltaTime;
        public double TravelTime => 0;
        public double TravelDistance => 0;
        public Vector2? LazyEndPosition => null;
        public double LazyTravelDistance => 0;
        public double LazyTravelTime => 0;

        public double? Angle => null;
        public double JumpDistance => Distance;
        public double LastObjectEndDeltaTime => 0;

        public double GetDoubletapness(OsuDifficultyHitObject osuDifficultyHitObject) => 0;
    }
}
