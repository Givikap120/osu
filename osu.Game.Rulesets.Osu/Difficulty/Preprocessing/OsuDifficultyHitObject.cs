// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Preprocessing
{
    public class OsuDifficultyHitObject : DifficultyHitObject
    {
        private const int normalized_radius = 52;

        protected new OsuHitObject BaseObject => (OsuHitObject)base.BaseObject;

        /// <summary>
        /// Normalized distance from the end position of the previous <see cref="OsuDifficultyHitObject"/> to the start position of this <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double JumpDistance { get; private set; }

        /// <summary>
        /// Normalized distance between the start and end position of the previous <see cref="OsuDifficultyHitObject"/>.
        /// </summary>
        public double TravelDistance { get; private set; }

        /// <summary>
        /// Angle the player has to take to hit this <see cref="OsuDifficultyHitObject"/>.
        /// Calculated as the angle between the circles (current-2, current-1, current).
        /// </summary>
        public double? Angle { get; private set; }

        /// <summary>
        /// Milliseconds elapsed since the start time of the previous <see cref="OsuDifficultyHitObject"/>, with a minimum of 50ms.
        /// </summary>
        public readonly double StrainTime;

        private readonly OsuHitObject? lastLastObject;
        private readonly OsuHitObject lastObject;

        public OsuDifficultyHitObject(HitObject hitObject, HitObject lastObject, HitObject? lastLastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
            this.lastLastObject = lastLastObject as OsuHitObject;
            this.lastObject = (OsuHitObject)lastObject;

            setDistances();

            // Every strain interval is hard capped at the equivalent of 375 BPM streaming speed as a safety measure
            StrainTime = Math.Max(50, DeltaTime);
        }

        private void setDistances()
        {
            // We will scale distances by this factor, so we can assume a uniform CircleSize among beatmaps.
            float scalingFactor = normalized_radius / (float)BaseObject.Radius;

            if (BaseObject.Radius < 30)
            {
                float smallCircleBonus = Math.Min(30 - (float)BaseObject.Radius, 5) / 50;
                scalingFactor *= 1 + smallCircleBonus;
            }

            if (lastObject is Slider lastSlider)
            {
                computeSliderCursorPosition(lastSlider);
                TravelDistance = lastSlider.LazyTravelDistance * scalingFactor;
            }

            Vector2 lastCursorPosition = getEndCursorPosition(lastObject);

            // Don't need to jump to reach spinners
            if (!(BaseObject is Spinner))
                JumpDistance = (BaseObject.StackedPosition * scalingFactor - lastCursorPosition * scalingFactor).Length;

            if (lastLastObject != null)
            {
                Vector2 lastLastCursorPosition = getEndCursorPosition(lastLastObject);

                Vector2 v1 = lastLastCursorPosition - lastObject.StackedPosition;
                Vector2 v2 = BaseObject.StackedPosition - lastCursorPosition;

                float dot = Vector2.Dot(v1, v2);
                float det = v1.X * v2.Y - v1.Y * v2.X;

                Angle = Math.Abs(Math.Atan2(det, dot));
            }
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
                var diff = slider.StackedPositionAt(progress) - slider.LazyEndPosition.Value;
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

        private Vector2 getEndCursorPosition(OsuHitObject hitObject)
        {
            Vector2 pos = hitObject.StackedPosition;

            if (hitObject is Slider slider)
            {
                computeSliderCursorPosition(slider);
                pos = slider.LazyEndPosition ?? pos;
            }

            return pos;
        }

        // To avoid compile errors with osu-tools
        public double AdjustedDeltaTime => StrainTime;
        public double LazyJumpDistance => JumpDistance;
        public double MinimumJumpDistance => JumpDistance;
        public double MinimumJumpTime => DeltaTime;
        public double TravelTime => 0;
        public Vector2? LazyEndPosition => null;
        public double LazyTravelDistance => 0;
        public double LazyTravelTime => 0;
        public double LastObjectEndDeltaTime => 0;

        public double GetDoubletapness(OsuDifficultyHitObject osuDifficultyHitObject) => 0;
    }
}
